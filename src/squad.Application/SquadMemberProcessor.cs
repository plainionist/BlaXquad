using System.Threading.Channels;
using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Application;

/// <summary>
/// One member's independent, single-reader, bounded mailbox, and the sole mutable accessor of this member's
/// <see cref="SquadMemberAggregate"/> - no other type ever reads or writes it. A provider-event or session-terminal
/// message never awaits provider I/O, so the read loop applies it inline, in order. A prompt, abort, or
/// interaction-response message removes any pending interaction it replaces inline on the read loop, then runs its
/// provider I/O detached from the loop - so a slow or blocked provider call for this member can never delay this
/// member's own next message, and can never delay any other member's processor, each of which owns an entirely
/// independent mailbox and read loop - and reports back through this same mailbox: once its admission gates (prompt
/// lease, abort wait, operation lease) clear via <see cref="OperationStartingMessage"/>, and once it concludes via
/// <see cref="OperationOutcomeMessage"/>. Both carry this processor's generation and the identity of the operation
/// they belong to, and the read loop applies their mutation only when that identity still names this member's
/// active operation - assigned the instant the loop dispatches a new operation - so a start or completion mutation
/// superseded by a later dispatch can never reopen canceled or terminal work, even though the loop still always
/// resolves the caller's own completion.
/// </summary>
internal sealed class SquadMemberProcessor : IDisposable
{
    // Bounded so a caller posting a message awaits mailbox capacity (backpressure) rather than an unbounded queue
    // growing without limit; multiple senders write concurrently (public commands and this processor's own
    // detached operations reporting their start and outcome), so SingleWriter is false.
    private readonly Channel<SquadMemberMessage> myMailbox = Channel.CreateBounded<SquadMemberMessage>(
        new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });

    private readonly object myInFlightLock = new();
    private readonly HashSet<Task> myInFlight = [];
    // Guarded by myInFlightLock; caches the one retirement so a repeated RetireAsync call - the view model both
    // stops and later disposes every member - observes the same task instead of posting a second sentinel.
    private Task? myRetirement;

    // The identity of the squad generation this processor belongs to, shared by every member of that generation
    // and carried on every start/outcome message so a mutation captured before retirement is rejected.
    private readonly SquadGenerationId myGeneration;
    // Read and written only by the read loop itself - assigned the instant a message dispatches a new operation -
    // so no lock is needed and no start or outcome message can ever race its own staleness check.
    private Guid myActiveOperationId = Guid.Empty;

    private readonly object myAdmissionLock;
    private readonly Func<bool> myIsAcceptingUnlocked;
    private readonly CancellationToken myShutdownToken;
    private readonly Action<bool> myNotifyStateChanged;
    private readonly Action<TranscriptUpdate> myTranscriptChanged;
    private readonly Task myLoop;

    internal SquadMemberProcessor(
        SquadMemberAggregate aggregate,
        object admissionLock,
        Func<bool> isAcceptingUnlocked,
        CancellationToken shutdownToken,
        Action<bool> notifyStateChanged,
        Action<TranscriptUpdate> transcriptChanged)
    {
        Aggregate = aggregate;
        myGeneration = aggregate.Generation;
        myAdmissionLock = admissionLock;
        myIsAcceptingUnlocked = isAcceptingUnlocked;
        myShutdownToken = shutdownToken;
        myNotifyStateChanged = notifyStateChanged;
        myTranscriptChanged = transcriptChanged;
        myLoop = RunLoopAsync();
    }

    /// <summary>
    /// This member's authoritative domain state. Exposed for read-only snapshot and query composition; every
    /// mutation of it happens inside this processor, reached only through the members below.
    /// </summary>
    internal SquadMemberAggregate Aggregate { get; }

    internal Task SendPromptAsync(string prompt, CancellationToken cancellationToken) =>
        PostAsync(completion => new SendPromptMessage(PromptKind.Prompt, prompt, cancellationToken, completion));

    internal Task SendHarnessAsync(string prompt, CancellationToken cancellationToken) =>
        PostAsync(completion => new SendPromptMessage(PromptKind.Harness, prompt, cancellationToken, completion));

    /// <summary>
    /// Coalesces concurrent aborts for this member, invalidating event admission and cancelling the active local
    /// operation atomically before routing the provider abort itself to the read loop. Events remain invalidated
    /// after a failed abort until a later abort succeeds.
    /// </summary>
    internal async Task AbortAsync(CancellationToken cancellationToken)
    {
        var lease = Aggregate.TryBeginAbort(out var existingAbort);
        if (lease is null)
        {
            await existingAbort!.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using (lease)
        {
            try
            {
                await PostAsync(completion => new AbortMessage(cancellationToken, completion)).ConfigureAwait(false);
                lease.Complete();
            }
            catch (Exception exception)
            {
                lease.Fail(exception);
                throw;
            }
        }
    }

    internal Task CompletePermissionAsync(InteractionRequestId requestId, AgentPermissionResponse response, CancellationToken cancellationToken) =>
        PostAsync(completion => new CompletePermissionMessage(requestId, response, cancellationToken, completion));

    internal Task CompleteInputAsync(InteractionRequestId requestId, AgentInputResponse response, CancellationToken cancellationToken) =>
        PostAsync(completion => new CompleteInputMessage(requestId, response, cancellationToken, completion));

    internal Task CompleteElicitationAsync(InteractionRequestId requestId, AgentElicitationResponse response, CancellationToken cancellationToken) =>
        PostAsync(completion => new CompleteElicitationMessage(requestId, response, cancellationToken, completion));

    internal Task ApplyEventAsync(AgentEvent agentEvent) =>
        PostAsync(completion => new ApplyProviderEventMessage(agentEvent, completion));

    internal Task MarkFailedAsync(Exception exception) =>
        PostAsync(completion => new SessionTerminalMessage(exception, completion));

    /// <summary>Sets this member's active provider session under the same lock guarding admission and selection.</summary>
    internal void SetSession(IAgentSession session)
    {
        lock (myAdmissionLock)
        {
            Contract.Requires(
                Aggregate.Session is null || Aggregate.Session.Completion.IsCompleted,
                $"Role '{Aggregate.Id}' already has a live provider session.");
            Aggregate.Session = session;
        }
    }

    /// <summary>Cancels this member's pending provider interactions, then clears them locally. Used during shutdown.</summary>
    internal async Task CancelPendingInteractionsAsync()
    {
        IAgentSession? session;
        lock (myAdmissionLock)
            session = Aggregate.Session;
        if (session is not null && !session.Completion.IsCompleted)
        {
            await session.CancelPendingInteractionsAsync().ConfigureAwait(false);
        }
        Aggregate.ClearInteractions();
        myNotifyStateChanged(true);
    }

    /// <summary>
    /// Closes this member's mailbox to further posts once every operation a message already queued at retirement
    /// will ever start has been dispatched. Posts a sentinel first - because the mailbox is FIFO and single-reader,
    /// it is read only after every message already queued when retirement begins, so once it resolves,
    /// <see cref="myInFlight"/> can never grow further and its snapshot is final. Waits for exactly those
    /// operations - each of which is guaranteed to succeed posting its own start and outcome messages, since the
    /// mailbox is not yet closed - before completing the writer and waiting for the loop to drain and exit. Caches
    /// its task so repeated calls - the view model both stops and later disposes every member - observe the same
    /// single retirement rather than a second call posting a sentinel to an already-closed mailbox.
    /// </summary>
    internal Task RetireAsync()
    {
        lock (myInFlightLock)
            return myRetirement ??= RetireCoreAsync();
    }

    private async Task RetireCoreAsync()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await myMailbox.Writer.WriteAsync(new RetirementSentinelMessage(drained)).ConfigureAwait(false);
        await drained.Task.ConfigureAwait(false);

        Task[] inFlight;
        lock (myInFlightLock)
            inFlight = myInFlight.ToArray();
        await Task.WhenAll(inFlight).ConfigureAwait(false);

        myMailbox.Writer.TryComplete();
        await myLoop.ConfigureAwait(false);
    }

    public void Dispose() => Aggregate.Dispose();

    private async Task PostAsync(Func<TaskCompletionSource, SquadMemberMessage> createMessage)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await myMailbox.Writer.WriteAsync(createMessage(completion)).ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }

    private async Task RunLoopAsync()
    {
        await foreach (var message in myMailbox.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            switch (message)
            {
                case ApplyProviderEventMessage applyProviderEvent:
                    RunInline(() => ApplyProjectedEvent(applyProviderEvent.Event), applyProviderEvent.Completion);
                    break;
                case SessionTerminalMessage sessionTerminal:
                    RunInline(() => MarkFailedCore(sessionTerminal.Failure), sessionTerminal.Completion);
                    break;
                case SendPromptMessage sendPrompt:
                    DispatchDetached(operationId => ExecutePromptAsync(
                        operationId, sendPrompt.Kind, sendPrompt.Prompt, sendPrompt.CancellationToken, sendPrompt.Completion));
                    break;
                case AbortMessage abort:
                    DispatchDetached(operationId => ExecuteAbortAsync(operationId, abort.CancellationToken, abort.Completion));
                    break;
                case CompletePermissionMessage completePermission:
                    CompleteInteraction<AgentPermissionRequest>(
                        completePermission.RequestId, completePermission.CancellationToken, completePermission.Completion,
                        (session, token) => session.RespondToPermissionAsync(completePermission.RequestId, completePermission.Response, token),
                        publishAnswer: null);
                    break;
                case CompleteInputMessage completeInput:
                    CompleteInteraction<AgentInputRequest>(
                        completeInput.RequestId, completeInput.CancellationToken, completeInput.Completion,
                        (session, token) => session.RespondToInputAsync(completeInput.RequestId, completeInput.Response, token),
                        publishAnswer: () => PublishInputAnswerTranscriptEntry(completeInput.Response));
                    break;
                case CompleteElicitationMessage completeElicitation:
                    CompleteInteraction<AgentElicitationRequest>(
                        completeElicitation.RequestId, completeElicitation.CancellationToken, completeElicitation.Completion,
                        (session, token) => session.RespondToElicitationAsync(completeElicitation.RequestId, completeElicitation.Response, token),
                        publishAnswer: null);
                    break;
                case OperationStartingMessage starting:
                    if (starting.Generation == myGeneration && starting.OperationId == myActiveOperationId)
                    {
                        starting.Apply();
                    }
                    starting.Applied.TrySetResult();
                    break;
                case OperationOutcomeMessage outcome:
                    if (outcome.ApplyMutation is not null
                        && (outcome.Unconditional || (outcome.Generation == myGeneration && outcome.OperationId == myActiveOperationId)))
                    {
                        outcome.ApplyMutation();
                    }
                    outcome.ResolveCompletion();
                    break;
                case RetirementSentinelMessage sentinel:
                    sentinel.Drained.TrySetResult();
                    break;
            }
        }
    }

    /// <summary>Runs a fast, provider-I/O-free message body inline, in order, on the read loop.</summary>
    private static void RunInline(Action action, TaskCompletionSource completion)
    {
        try
        {
            action();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    /// <summary>
    /// Assigns a fresh operation identity as this member's active operation - the instant the read loop dispatches
    /// it, before any of its admission gates or provider I/O run - so any earlier operation's still-pending start or
    /// outcome message is unambiguously superseded, then starts its body without the read loop awaiting it.
    /// </summary>
    private void DispatchDetached(Func<Guid, Task> operation)
    {
        var operationId = Guid.NewGuid();
        myActiveOperationId = operationId;
        var operationTask = operation(operationId);
        lock (myInFlightLock)
            myInFlight.Add(operationTask);
        _ = ObserveDetachedAsync(operationTask);
    }

    private async Task ObserveDetachedAsync(Task operationTask)
    {
        try
        {
            await operationTask.ConfigureAwait(false);
        }
        catch
        {
            // Every Execute*Async method reports its own outcome through PostOutcomeAsync; this only prevents an
            // unobserved-exception crash from the detached task itself.
        }
        finally
        {
            lock (myInFlightLock)
                myInFlight.Remove(operationTask);
        }
    }

    /// <summary>Transitions the pending interaction this message replaces to responding, inline, in order, on the read loop.</summary>
    private void CompleteInteraction<TRequest>(
        InteractionRequestId requestId,
        CancellationToken cancellationToken,
        TaskCompletionSource completion,
        Func<IAgentSession, CancellationToken, Task> respond,
        Action? publishAnswer)
    {
        try
        {
            Aggregate.BeginResponding<TRequest>(requestId);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            return;
        }
        DispatchDetached(operationId => ExecuteCompleteInteractionAsync(
            operationId, requestId, cancellationToken, completion, respond, publishAnswer));
    }

    private async Task ExecutePromptAsync(
        Guid operationId, PromptKind kind, string prompt, CancellationToken cancellationToken, TaskCompletionSource completion)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdownToken);
        try
        {
            using var promptLease = await Aggregate.AcquirePromptLeaseAsync(lifetimeCancellation.Token).ConfigureAwait(false);
            EnsureRoleAvailable();
            await Aggregate.WaitForAbortAsync(lifetimeCancellation.Token).ConfigureAwait(false);

            // Resumes event admission and marks this member working before this operation ever competes for the
            // operation slot below - mirroring this member's original timing for the two mutations, so a stale
            // "invalidated" flag left by an abort that has already finished can never cancel this fresh
            // operation's own token the instant it registers for that slot.
            await PostOperationStartingAsync(operationId, () =>
            {
                Aggregate.ResumeEvents();
                SetWorking();
            }).ConfigureAwait(false);

            EnsureAccepting();
            if (!TryCaptureSession(out var session))
            {
                throw new InvalidOperationException($"Unknown role: {Aggregate.Id}");
            }
            using var operationLease = await AcquireOperationAsync(lifetimeCancellation).ConfigureAwait(false);

            if (kind == PromptKind.Harness)
            {
                await session.SendHarnessAsync(prompt, lifetimeCancellation.Token).ConfigureAwait(false);
            }
            else
            {
                await session.SendAsync(prompt, lifetimeCancellation.Token).ConfigureAwait(false);
            }
            await PostOutcomeAsync(operationId, applyMutation: null, () => completion.TrySetResult()).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            await PostOutcomeAsync(operationId, applyMutation: null, () => completion.TrySetCanceled(exception.CancellationToken)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await PostOutcomeAsync(operationId, applyMutation: null, () => completion.TrySetException(exception)).ConfigureAwait(false);
        }
    }

    private async Task ExecuteAbortAsync(Guid operationId, CancellationToken cancellationToken, TaskCompletionSource completion)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdownToken);
        Exception? failure = null;
        try
        {
            EnsureAccepting();
            if (!TryCaptureSession(out var session))
            {
                throw new InvalidOperationException($"Unknown role: {Aggregate.Id}");
            }
            using var operationLease = await AcquireOperationAsync(lifetimeCancellation).ConfigureAwait(false);
            await session.CancelPendingInteractionsAsync(CancellationToken.None).ConfigureAwait(false);
            await session.AbortAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // Always applied: this abort's own cleanup must land even when a later operation, dispatched while this
        // abort's provider call was still in flight, has since become this member's active operation.
        await PostOutcomeAsync(
            operationId,
            applyMutation: () =>
            {
                RemovePendingInteractions();
                SetIdle();
                myNotifyStateChanged(true);
            },
            () => ResolveOutcome(completion, failure),
            unconditional: true).ConfigureAwait(false);
    }

    private async Task ExecuteCompleteInteractionAsync(
        Guid operationId,
        InteractionRequestId requestId,
        CancellationToken cancellationToken,
        TaskCompletionSource completion,
        Func<IAgentSession, CancellationToken, Task> respond,
        Action? publishAnswer)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdownToken);
        Exception? failure = null;
        try
        {
            EnsureAccepting();
            if (!TryCaptureSession(out var session))
            {
                throw new InvalidOperationException($"Unknown role: {Aggregate.Id}");
            }
            using var operationLease = await AcquireOperationAsync(lifetimeCancellation).ConfigureAwait(false);
            await respond(session, lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await PostOutcomeAsync(
            operationId,
            applyMutation: () =>
            {
                if (failure is null)
                {
                    publishAnswer?.Invoke();
                    UnprotectPendingTranscriptEntry(requestId);
                }
                else if (!Aggregate.IsFailed)
                {
                    Aggregate.RestorePending(requestId);
                }
                else
                {
                    UnprotectPendingTranscriptEntry(requestId);
                }
                myNotifyStateChanged(true);
            },
            () => ResolveOutcome(completion, failure)).ConfigureAwait(false);
    }

    private static void ResolveOutcome(TaskCompletionSource completion, Exception? failure)
    {
        switch (failure)
        {
            case null:
                completion.TrySetResult();
                break;
            case OperationCanceledException canceled:
                completion.TrySetCanceled(canceled.CancellationToken);
                break;
            default:
                completion.TrySetException(failure);
                break;
        }
    }

    /// <summary>
    /// Waits for this member's operation serialization slot, re-checks admission now that the wait has passed, and
    /// registers this operation's cancellation with the aggregate so a concurrent abort can cancel it while it
    /// holds the slot.
    /// </summary>
    private async Task<OperationLease> AcquireOperationAsync(CancellationTokenSource lifetimeCancellation)
    {
        var operationLease = await Aggregate.AcquireOperationLeaseAsync(lifetimeCancellation.Token).ConfigureAwait(false);
        try
        {
            EnsureAccepting();
            EnsureRoleAvailable();
            operationLease.Register(lifetimeCancellation);
            return operationLease;
        }
        catch
        {
            operationLease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Posts <paramref name="mutation"/> back through this same mailbox as an <see cref="OperationStartingMessage"/>
    /// so applying it - like every other aggregate mutation - happens on the read loop, in order, and awaits that
    /// message being processed before letting the caller proceed. Applied only while <paramref name="operationId"/>
    /// still names this member's active operation, so a caller superseded by a later dispatch while it was still
    /// waiting out its own prompt lease or an in-flight abort can never re-open state for an operation that no
    /// longer owns this member.
    /// </summary>
    private async Task PostOperationStartingAsync(Guid operationId, Action mutation)
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await myMailbox.Writer.WriteAsync(
            new OperationStartingMessage(myGeneration, Aggregate.Id, operationId, mutation, applied)).ConfigureAwait(false);
        await applied.Task.ConfigureAwait(false);
    }

    private Task PostOutcomeAsync(Guid operationId, Action? applyMutation, Action resolveCompletion, bool unconditional = false) =>
        myMailbox.Writer.WriteAsync(
            new OperationOutcomeMessage(myGeneration, Aggregate.Id, operationId, applyMutation, unconditional, resolveCompletion)).AsTask();

    /// <summary>
    /// Atomically checks admission and captures this member's current non-terminal session under
    /// <see cref="myAdmissionLock"/> - the same synchronization boundary <see cref="SetSession"/> uses, so a
    /// stopping transition can never interleave with session selection.
    /// </summary>
    private bool TryCaptureSession(out IAgentSession session)
    {
        lock (myAdmissionLock)
        {
            if (myIsAcceptingUnlocked() && Aggregate.Session is { } candidate && !candidate.Completion.IsCompleted)
            {
                session = candidate;
                return true;
            }
        }
        session = null!;
        return false;
    }

    private void EnsureAccepting()
    {
        lock (myAdmissionLock)
            if (!myIsAcceptingUnlocked())
            {
                throw new OperationCanceledException("Squad is shutting down");
            }
    }

    private void EnsureRoleAvailable()
    {
        if (!Aggregate.IsFailed)
        {
            return;
        }
        lock (Aggregate.SyncRoot)
            throw new InvalidOperationException($"Role '{Aggregate.Id}' is unavailable: {Aggregate.Error}");
    }

    private void ApplyProjectedEvent(AgentEvent agentEvent)
    {
        if (Aggregate.IsFailed || ShouldIgnoreEvent(agentEvent))
        {
            return;
        }
        TranscriptUpdate? transcriptUpdate;
        lock (Aggregate.SyncRoot)
        {
            transcriptUpdate = SquadMemberEventProjector.Project(Aggregate, agentEvent);
            if (transcriptUpdate is not null)
            {
                myTranscriptChanged(transcriptUpdate);
            }
        }
        myNotifyStateChanged(IsImmediateUiEvent(agentEvent));
    }

    private bool ShouldIgnoreEvent(AgentEvent agentEvent)
    {
        if (agentEvent is AgentStartedEvent or AgentStoppedEvent or AgentSessionConfigurationEvent
            or AgentSessionModelChangedEvent or AgentContextUsageEvent or AgentSessionUsageEvent)
        {
            return false;
        }
        return Aggregate.IsInvalidated;
    }

    private void MarkFailedCore(Exception exception)
    {
        Aggregate.MarkFailed();
        RemovePendingInteractions();
        lock (Aggregate.SyncRoot)
        {
            Aggregate.Status = SquadMemberStatus.Error;
            Aggregate.Error = exception.Message;
            Aggregate.IsWorking = false;
            Aggregate.ActiveTool = null;
        }
        myNotifyStateChanged(true);
    }

    private void SetWorking()
    {
        lock (Aggregate.SyncRoot)
        {
            Aggregate.IsWorking = true;
            Aggregate.ActiveTool = null;
        }
        myNotifyStateChanged(true);
    }

    private void SetIdle()
    {
        lock (Aggregate.SyncRoot)
        {
            Aggregate.IsWorking = false;
            Aggregate.ActiveTool = null;
        }
    }

    private void RemovePendingInteractions()
    {
        foreach (var entryIndex in Aggregate.RemoveAllInteractions())
        {
            lock (Aggregate.SyncRoot)
                Aggregate.Transcript.UnprotectTranscriptEntry(entryIndex);
        }
    }

    private void UnprotectPendingTranscriptEntry(InteractionRequestId requestId)
    {
        var entryIndex = Aggregate.TryRemoveProtectedTranscriptEntry(requestId);
        if (entryIndex is null)
        {
            return;
        }
        lock (Aggregate.SyncRoot)
            Aggregate.Transcript.UnprotectTranscriptEntry(entryIndex.Value);
    }

    /// <summary>
    /// Appends the user's accepted input answer as a normal "user" transcript entry, reusing the same transcript
    /// mutation and notification path as every other transcript source so the entry participates in live updates,
    /// retention, paging, and reconnect recovery. Only reached after <see cref="IAgentSession.RespondToInputAsync"/>
    /// has completed successfully; permission and elicitation responses never call this.
    /// </summary>
    private void PublishInputAnswerTranscriptEntry(AgentInputResponse response)
    {
        if (response.Answer is null)
        {
            return;
        }
        lock (Aggregate.SyncRoot)
        {
            var transcriptUpdate = Aggregate.Transcript.AddTranscriptEntry(new TranscriptEntry(DateTimeOffset.UtcNow, TranscriptSource.User, response.Answer));
            myTranscriptChanged(transcriptUpdate);
        }
    }

    private static bool IsImmediateUiEvent(AgentEvent agentEvent) =>
        agentEvent is AgentErrorEvent or AgentIdleEvent or AgentStoppedEvent
            or AgentPermissionRequest or AgentInputRequest or AgentElicitationRequest;
}
