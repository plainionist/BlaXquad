using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application.Events;
using squad.Application.Interactions;
using squad.Application.RoleOperations;
using squad.Application.Transcripts;
using squad.Ui.Abstractions;
using System.Text.Json;
using System.Threading.Channels;

namespace squad.Application;

/// <summary>
/// Serializes provider events and user commands into authoritative per-role state, transcript history, and pending
/// interactions while publishing UI refresh signals.
/// </summary>
public sealed class SquadViewModel : ISquadUi, ITranscriptUi, IAsyncDisposable
{
    private readonly Channel<Func<Task>> myCommands = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource myShutdown = new();
    private readonly Dictionary<string, IAgentSession> mySessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentRoleState> myRoles = new(StringComparer.Ordinal);
    private readonly List<string> myRoleOrder = [];
    private string myLeader = "";
    private readonly TranscriptArchive myTranscriptArchive;
    private readonly TranscriptRetentionOptions myTranscriptRetentionOptions;
    private readonly RoleOperationCoordinator myRoleOperations = new();
    private readonly PendingInteractionRegistry myInteractions = new();
    private readonly AgentEventProjector myEventProjector;
    private readonly Task myEventLoop;
    // The one synchronization boundary for command admission and active-session selection: myAccepting and
    // mySessions are read and written only while holding this lock, so a stopping transition and a session
    // capture can never interleave.
    private readonly object myAdmissionLock = new();
    private readonly HashSet<Task> myAcceptedCommands = [];
    private bool myAccepting = true;

    public SquadViewModel()
    {
        myTranscriptRetentionOptions = new TranscriptRetentionOptions();
        myTranscriptArchive = new TranscriptArchive(myTranscriptRetentionOptions);
        myEventProjector = new AgentEventProjector(myInteractions);
        myEventLoop = RunEventLoopAsync();
    }

    public event Action<UiRefreshPriority>? SnapshotRequested;
    public event Action<TranscriptUpdate>? TranscriptChanged;

    public void InitializeRoles(IEnumerable<string> roleNames)
    {
        foreach (var role in roleNames)
        {
            if (myRoles.TryAdd(role, new AgentRoleState(role, myTranscriptArchive, myTranscriptRetentionOptions)))
            {
                myRoleOrder.Add(role);
            }
        }
        NotifyStateChanged();
    }

    /// <summary>Records the configured leader role name for publication as authoritative session metadata.</summary>
    public void SetLeader(string leader)
    {
        myLeader = leader;
        NotifyStateChanged();
    }

    public JsonElement CreateSnapshot()
    {
        // Enumerate in configured role order (myRoleOrder), not myRoles.Values, so state.snapshot.roles matches
        // blaxquad/squad.json regardless of Dictionary enumeration behavior.
        var roles = myRoleOrder.Select(role => myRoles[role].CreateSnapshot()).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            leader = myLeader,
            roles = roles.Select(role => new
            {
                role = role.Role,
                status = role.Status,
                lastEventAt = role.LastEventAt,
                error = role.Error,
                activeTool = role.ActiveTool,
                isWorking = role.IsWorking,
                model = role.Model,
                effort = role.Effort,
                aicUsed = role.AicUsed,
                contextUsedTokens = role.ContextUsedTokens,
                contextLimitTokens = role.ContextLimitTokens,
                eventCount = role.EventCount,
            }),
            permissions = myInteractions.Permissions.Select(permission => new
            {
                requestId = permission.RequestId,
                role = permission.Role,
                description = permission.Description,
            }),
            inputs = myInteractions.Inputs.Select(input => new
            {
                requestId = input.RequestId,
                role = input.Role,
                prompt = input.Prompt,
                choices = input.Choices,
                allowFreeform = input.AllowFreeform,
            }),
            elicitations = myInteractions.Elicitations.Select(elicitation => new
            {
                requestId = elicitation.RequestId,
                role = elicitation.Role,
                prompt = elicitation.Prompt,
                mode = elicitation.Mode,
                requestedSchema = elicitation.RequestedSchema,
                url = elicitation.Url,
            }),
        });
    }

    public IReadOnlyList<RoleTranscriptSnapshot> CreateTranscriptSnapshot(int maxEntriesPerRole)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntriesPerRole);
        return myRoleOrder
            .Select(role => myRoles[role])
            .Select(role => role.Transcript.CreateTranscriptSnapshot(maxEntriesPerRole))
            .ToArray();
    }

    public RoleTranscriptPage CreateTranscriptPage(string role, int beforeIndex, int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(beforeIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        if (!myRoles.TryGetValue(role, out var state))
        {
            throw new InvalidOperationException($"Unknown role: {role}");
        }
        return state.Transcript.CreateTranscriptPage(beforeIndex, maxEntries);
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(string role, int entryIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        if (!myRoles.TryGetValue(role, out var state))
        {
            throw new InvalidOperationException($"Unknown role: {role}");
        }
        return state.Transcript.CreateArchivedTranscriptEntry(entryIndex);
    }

    public AgentElicitationRequest GetPendingElicitation(string role, string requestId) =>
        myInteractions.GetElicitation(role, requestId);

    /// <summary>
    /// Returns <see langword="null"/> for an unknown role, <see langword="false"/> when work is inadmissible, and
    /// otherwise the readiness inferred from serialized local state.
    /// </summary>
    private bool? GetRoleReadiness(string role)
    {
        if (!myRoles.TryGetValue(role, out var state))
        {
            return null;
        }
        if (!IsAccepting)
        {
            return false;
        }
        if (myRoleOperations.IsInvalidated(role))
        {
            return false;
        }
        lock (state.SyncRoot)
            return state.Status == "idle" && !state.IsWorking;
    }

    /// <summary>
    /// Returns the tri-state local readiness result derived purely from already-projected session, prompt, idle,
    /// stopped, and failed events - the only readiness source `wait-for-agent` observes.
    /// </summary>
    public Task<bool?> GetRoleReadinessAsync(
        string role,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GetRoleReadiness(role));

    /// <summary>Registers a role's active provider session under the same lock guarding admission and selection.</summary>
    public void RegisterSession(IAgentSession session)
    {
        lock (myAdmissionLock)
            mySessions[session.Role] = session;
    }

    public Task MarkRoleFailedAsync(string role, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return EnqueueCoreAsync(() =>
        {
            if (!myRoles.TryGetValue(role, out var state))
            {
                return Task.CompletedTask;
            }
            myRoleOperations.MarkRoleFailed(role);
            RemovePendingInteractionsForRole(role);
            lock (state.SyncRoot)
            {
                state.Status = "error";
                state.Error = exception.Message;
                state.IsWorking = false;
                state.ActiveTool = null;
            }
            NotifyStateChanged();
            return Task.CompletedTask;
        }, myShutdown.Token);
    }

    private void BeginStopping()
    {
        lock (myAdmissionLock)
            myAccepting = false;
        myCommands.Writer.TryComplete();
        myShutdown.Cancel();
    }

    public Task SendAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        TrackCommand(() => DispatchPromptAsync(role, (session, token) => session.SendAsync(prompt, token), cancellationToken));

    public Task SendHarnessAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        TrackCommand(() => DispatchPromptAsync(role, (session, token) => session.SendHarnessAsync(prompt, token), cancellationToken));

    /// <summary>
    /// Coalesces concurrent aborts for a role, cancels its active local operation, and waits for the provider abort.
    /// Events remain invalidated after a failed abort until a later abort succeeds.
    /// </summary>
    public Task AbortAsync(string role, CancellationToken cancellationToken = default) =>
        AbortRoleAndWaitAsync(role, cancellationToken);

    public Task CompletePermissionAsync(string role, string requestId, bool approved, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompletePermissionCoreAsync(role, requestId, new AgentPermissionResponse(approved), cancellationToken));

    public Task CompleteInputAsync(string role, string requestId, string? answer, bool wasFreeform, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompleteInputCoreAsync(role, requestId, new AgentInputResponse(answer, wasFreeform), cancellationToken));

    public Task CompleteElicitationAsync(string role, string requestId, string action, JsonElement? content, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompleteElicitationCoreAsync(role, requestId, new AgentElicitationResponse(action, content), cancellationToken));

    public Task EnqueueEventAsync(string role, AgentEvent agentEvent, CancellationToken cancellationToken = default) =>
        TrackCommand(() => EnqueueCoreAsync(() => ApplyEventAsync(role, agentEvent), cancellationToken));

    /// <summary>
    /// Stops accepting work, cancels pending provider interactions, and waits for every command accepted before
    /// shutdown. Repeated calls are safe.
    /// </summary>
    public async Task StopAsync()
    {
        BeginStopping();
        await CancelAllPendingInteractionsAsync();
        Task[] accepted;
        lock (myAdmissionLock)
            accepted = myAcceptedCommands.ToArray();
        try
        {
            await Task.WhenAll(accepted);
        }
        catch (OperationCanceledException) when (myShutdown.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            try
            {
                await myEventLoop;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                myShutdown.Dispose();
                myRoleOperations.Dispose();
                myTranscriptArchive.Dispose();
            }
        }
    }

    private async Task EnqueueCoreAsync(Func<Task> command, CancellationToken cancellationToken)
    {
        EnsureAccepting();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shutdownRegistration = myShutdown.Token.Register(() => completion.TrySetCanceled(myShutdown.Token));
        await myCommands.Writer.WriteAsync(async () =>
        {
            try
            {
                await command();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, cancellationToken);
        await completion.Task;
    }

    private Task TrackCommand(Func<Task> operation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (myAdmissionLock)
        {
            if (!myAccepting)
            {
                return Task.FromException(new InvalidOperationException("Squad is shutting down"));
            }
            myAcceptedCommands.Add(completion.Task);
        }

        _ = CompleteTrackedCommandAsync(operation, completion);
        return completion.Task;
    }

    private async Task CompleteTrackedCommandAsync(Func<Task> operation, TaskCompletionSource completion)
    {
        try
        {
            await operation();
            completion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (myAdmissionLock)
                myAcceptedCommands.Remove(completion.Task);
        }
    }

    private async Task RunEventLoopAsync()
    {
        await foreach (var command in myCommands.Reader.ReadAllAsync(myShutdown.Token))
        {
            await command();
        }
    }

    private Task ApplyEventAsync(string role, AgentEvent agentEvent)
    {
        if (!myRoles.TryGetValue(role, out var state))
        {
            return Task.CompletedTask;
        }
        if (myRoleOperations.IsRoleFailed(role))
        {
            return Task.CompletedTask;
        }
        if (ShouldIgnoreEvent(role, agentEvent))
        {
            return Task.CompletedTask;
        }
        TranscriptUpdate? transcriptUpdate;
        lock (state.SyncRoot)
        {
            transcriptUpdate = myEventProjector.Project(state, agentEvent);
            if (transcriptUpdate is not null)
            {
                TranscriptChanged?.Invoke(transcriptUpdate);
            }
        }
        NotifyStateChanged(IsImmediateUiEvent(agentEvent));
        return Task.CompletedTask;
    }

    private async Task DispatchPromptAsync(string role, Func<IAgentSession, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdown.Token);
        using var promptLease = await myRoleOperations.AcquirePromptLeaseAsync(role, lifetimeCancellation.Token);
        EnsureRoleAvailable(role);
        await myRoleOperations.WaitForAbortAsync(role, lifetimeCancellation.Token);
        myRoleOperations.ResumeEvents(role);
        await EnqueueCoreAsync(() => MarkWaitingForResponseAsync(role), lifetimeCancellation.Token);
        await RunForRoleAsync(role, operation, lifetimeCancellation.Token);
    }

    private Task MarkWaitingForResponseAsync(string role)
    {
        if (myRoles.TryGetValue(role, out var state))
        {
            lock (state.SyncRoot)
            {
                state.IsWorking = true;
                state.ActiveTool = null;
            }
            NotifyStateChanged();
        }
        return Task.CompletedTask;
    }

    private async Task RunForRoleAsync(
        string role,
        Func<IAgentSession, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        EnsureAccepting();
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdown.Token);
        if (!TryCaptureSession(role, out var session))
        {
            throw new InvalidOperationException($"Unknown role: {role}");
        }
        using var operationLease = await myRoleOperations.AcquireOperationLeaseAsync(role, lifetimeCancellation.Token);
        EnsureAccepting();
        EnsureRoleAvailable(role);
        operationLease.Register(lifetimeCancellation);
        await operation(session, lifetimeCancellation.Token);
    }

    /// <summary>
    /// Atomically checks admission and captures the role's current non-terminal session under
    /// <see cref="myAdmissionLock"/> - the same synchronization boundary <see cref="TrackCommand"/> and
    /// <see cref="RegisterSession"/> use, so a stopping transition can never interleave with session selection.
    /// </summary>
    private bool TryCaptureSession(string role, out IAgentSession session)
    {
        lock (myAdmissionLock)
        {
            if (myAccepting
                && mySessions.TryGetValue(role, out var candidate)
                && !candidate.Completion.IsCompleted)
            {
                session = candidate;
                return true;
            }
        }
        session = null!;
        return false;
    }

    private void EnsureRoleAvailable(string role)
    {
        if (!myRoleOperations.IsRoleFailed(role))
        {
            return;
        }
        if (myRoles.TryGetValue(role, out var state))
        {
            lock (state.SyncRoot)
                throw new InvalidOperationException($"Role '{role}' is unavailable: {state.Error}");
        }
        throw new InvalidOperationException($"Role '{role}' is unavailable.");
    }

    private async Task AbortRoleAndWaitAsync(string role, CancellationToken cancellationToken)
    {
        var lease = myRoleOperations.TryBeginAbort(role, out var existingAbort);
        if (lease is null)
        {
            await existingAbort!.WaitAsync(cancellationToken);
            return;
        }

        using (lease)
        {
            try
            {
                await TrackCommand(() => AbortRoleAsync(role));
                lease.Complete();
            }
            catch (Exception exception)
            {
                lease.Fail(exception);
                throw;
            }
        }
    }

    private async Task AbortRoleAsync(string role)
    {
        try
        {
            await RunForRoleAsync(role, async (session, _) =>
            {
                await session.CancelPendingInteractionsAsync(CancellationToken.None);
                await session.AbortAsync(CancellationToken.None);
            }, CancellationToken.None);
        }
        finally
        {
            RemovePendingInteractionsForRole(role);
            MarkRoleIdle(role);
            NotifyStateChanged();
        }
    }

    private bool ShouldIgnoreEvent(string role, AgentEvent agentEvent)
    {
        if (agentEvent is AgentStartedEvent or AgentStoppedEvent or AgentSessionConfigurationEvent or AgentSessionModelChangedEvent or AgentContextUsageEvent or AgentSessionUsageEvent)
        {
            return false;
        }
        return myRoleOperations.IsInvalidated(role);
    }

    private void MarkRoleIdle(string role)
    {
        if (!myRoles.TryGetValue(role, out var state))
        {
            return;
        }
        lock (state.SyncRoot)
        {
            state.IsWorking = false;
            state.ActiveTool = null;
        }
    }

    private Task CompletePermissionCoreAsync(string expectedRole, string requestId, AgentPermissionResponse response, CancellationToken cancellationToken) =>
        EnqueueCoreAsync(() => CompleteInteractionCoreAsync(
            myInteractions.RemovePermission, myInteractions.RegisterPermission, expectedRole, requestId,
            (session, token) => session.RespondToPermissionAsync(requestId, response, token), onCompleted: null, cancellationToken), cancellationToken);

    private Task CompleteInputCoreAsync(string expectedRole, string requestId, AgentInputResponse response, CancellationToken cancellationToken) =>
        EnqueueCoreAsync(() => CompleteInteractionCoreAsync(
            myInteractions.RemoveInput, myInteractions.RegisterInput, expectedRole, requestId,
            (session, token) => session.RespondToInputAsync(requestId, response, token),
            role => PublishInputAnswerTranscriptEntry(role, response),
            cancellationToken), cancellationToken);

    private Task CompleteElicitationCoreAsync(string expectedRole, string requestId, AgentElicitationResponse response, CancellationToken cancellationToken) =>
        EnqueueCoreAsync(() => CompleteInteractionCoreAsync(
            myInteractions.RemoveElicitation, myInteractions.RegisterElicitation, expectedRole, requestId,
            (session, token) => session.RespondToElicitationAsync(requestId, response, token), onCompleted: null, cancellationToken), cancellationToken);

    private async Task CompleteInteractionCoreAsync<TRequest>(
        Func<string, string, (string Role, TRequest Request)> remove,
        Action<TRequest> restore,
        string expectedRole,
        string requestId,
        Func<IAgentSession, CancellationToken, Task> respond,
        Action<string>? onCompleted,
        CancellationToken cancellationToken)
    {
        var (role, request) = remove(expectedRole, requestId);
        try
        {
            await RunForRoleAsync(role, respond, cancellationToken);
            onCompleted?.Invoke(role);
            UnprotectPendingTranscriptEntry(role, requestId);
            NotifyStateChanged();
        }
        catch
        {
            if (!myRoleOperations.IsRoleFailed(role))
            {
                restore(request);
            }
            else
            {
                UnprotectPendingTranscriptEntry(role, requestId);
            }
            NotifyStateChanged();
            throw;
        }
    }

    /// <summary>
    /// Appends the user's accepted input answer as a normal "user" transcript entry, reusing the same transcript
    /// mutation and notification path as every other transcript source so the entry participates in live updates,
    /// retention, paging, and reconnect recovery. Only reached after <see cref="IAgentSession.RespondToInputAsync"/>
    /// has completed successfully; permission and elicitation responses never call this.
    /// </summary>
    private void PublishInputAnswerTranscriptEntry(string role, AgentInputResponse response)
    {
        if (response.Answer is null || !myRoles.TryGetValue(role, out var state))
        {
            return;
        }
        lock (state.SyncRoot)
        {
            var transcriptUpdate = state.Transcript.AddTranscriptEntry(new TranscriptEntry(DateTimeOffset.UtcNow, "user", response.Answer));
            TranscriptChanged?.Invoke(transcriptUpdate);
        }
    }

    private async Task CancelAllPendingInteractionsAsync()
    {
        IAgentSession[] sessions;
        lock (myAdmissionLock)
            sessions = mySessions.Values.ToArray();
        foreach (var session in sessions)
        {
            if (!session.Completion.IsCompleted)
            {
                await session.CancelPendingInteractionsAsync();
            }
        }
        myInteractions.Clear();
        NotifyStateChanged();
    }

    private void RemovePendingInteractionsForRole(string role)
    {
        foreach (var protectedEntry in myInteractions.RemoveForRole(role))
        {
            if (myRoles.TryGetValue(protectedEntry.Role, out var state))
            {
                lock (state.SyncRoot)
                    state.Transcript.UnprotectTranscriptEntry(protectedEntry.EntryIndex);
            }
        }
    }

    private void UnprotectPendingTranscriptEntry(string role, string requestId)
    {
        var protectedEntry = myInteractions.TryRemoveProtectedTranscriptEntry(role, requestId);
        if (protectedEntry is null)
        {
            return;
        }
        if (myRoles.TryGetValue(protectedEntry.Value.Role, out var state))
        {
            lock (state.SyncRoot)
                state.Transcript.UnprotectTranscriptEntry(protectedEntry.Value.EntryIndex);
        }
    }

    private bool IsAccepting
    {
        get { lock (myAdmissionLock) return myAccepting; }
    }

    private void EnsureAccepting()
    {
        if (!IsAccepting)
        {
            throw new InvalidOperationException("Squad is shutting down");
        }
    }

    private void NotifyStateChanged(bool immediate = true)
    {
        foreach (Action<UiRefreshPriority> listener in SnapshotRequested?.GetInvocationList() ?? [])
        {
            try
            {
                listener(immediate ? UiRefreshPriority.Immediate : UiRefreshPriority.Deferred);
            }
            catch
            {
            }
        }
    }

    private static bool IsImmediateUiEvent(AgentEvent agentEvent) =>
        agentEvent is AgentErrorEvent or AgentIdleEvent or AgentStoppedEvent
            or AgentPermissionRequest or AgentInputRequest or AgentElicitationRequest;
}
