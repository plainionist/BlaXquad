using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application.Members;
using squad.Application.Transcripts;
using squad.Ui.Abstractions;
using System.Text.Json;

namespace squad.Application;

/// <summary>
/// Serializes provider events and user commands into authoritative per-member state, transcript history, and
/// pending interactions while publishing UI refresh signals.
/// </summary>
public sealed class SquadViewModel : ISquadUi, ITranscriptUi, IAsyncDisposable
{
    private readonly CancellationTokenSource myShutdown = new();
    private readonly Dictionary<string, MemberAggregate> myMembers = new(StringComparer.Ordinal);
    // Each member's own independent mailbox and single-reader loop - the sole path through which a prompt, harness,
    // abort, interaction-response, provider-event, or session-terminal message reaches that member's MemberAggregate.
    // A slow or blocked provider call for one member can never delay another member's processor, and never delays
    // this member's own provider-event or session-terminal messages either, since those are applied inline without
    // awaiting provider I/O.
    private readonly Dictionary<string, MemberProcessor> myProcessors = new(StringComparer.Ordinal);
    private readonly List<string> myMemberOrder = [];
    private string myLeader = "";
    private readonly TranscriptArchive myTranscriptArchive;
    private readonly TranscriptRetentionOptions myTranscriptRetentionOptions;
    // The one synchronization boundary for command admission and active-session selection: myAccepting and each
    // member's Session are read and written only while holding this lock, so a stopping transition and a session
    // capture can never interleave.
    private readonly object myAdmissionLock = new();
    private bool myAccepting = true;

    public SquadViewModel()
    {
        myTranscriptRetentionOptions = new TranscriptRetentionOptions();
        myTranscriptArchive = new TranscriptArchive(myTranscriptRetentionOptions);
    }

    public event Action<UiRefreshPriority>? SnapshotRequested;
    public event Action<TranscriptUpdate>? TranscriptChanged;

    public void InitializeRoles(IEnumerable<MemberConfiguration> members)
    {
        foreach (var configuration in members)
        {
            var role = configuration.Member;
            if (!myMembers.TryAdd(role, new MemberAggregate(role, configuration.DisplayName, configuration.Role, myTranscriptArchive, myTranscriptRetentionOptions)))
            {
                continue;
            }
            myMemberOrder.Add(role);
            myProcessors.Add(role, new MemberProcessor(
                executePrompt: (operation, cancellationToken) => DispatchPromptAsync(role, operation, cancellationToken),
                executeAbort: _ => AbortRoleAsync(role),
                executeCompletePermission: (requestId, response, cancellationToken) => CompleteInteractionCoreAsync(
                    role, requestId,
                    m => m.RemovePermission(requestId),
                    (m, request) => m.RegisterPermission(request),
                    (session, token) => session.RespondToPermissionAsync(requestId, response, token),
                    onCompleted: null,
                    cancellationToken),
                executeCompleteInput: (requestId, response, cancellationToken) => CompleteInteractionCoreAsync(
                    role, requestId,
                    m => m.RemoveInput(requestId),
                    (m, request) => m.RegisterInput(request),
                    (session, token) => session.RespondToInputAsync(requestId, response, token),
                    completedRole => PublishInputAnswerTranscriptEntry(completedRole, response),
                    cancellationToken),
                executeCompleteElicitation: (requestId, response, cancellationToken) => CompleteInteractionCoreAsync(
                    role, requestId,
                    m => m.RemoveElicitation(requestId),
                    (m, request) => m.RegisterElicitation(request),
                    (session, token) => session.RespondToElicitationAsync(requestId, response, token),
                    onCompleted: null,
                    cancellationToken),
                applyEvent: agentEvent => ApplyProjectedEvent(role, agentEvent),
                markFailed: exception => MarkRoleFailedCore(role, exception)));
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
        // Enumerate in configured member order (myMemberOrder), not myMembers.Values, so state.snapshot.roles
        // matches blaxquad/squad.json regardless of Dictionary enumeration behavior.
        var members = myMemberOrder.Select(id => myMembers[id].CreateSnapshot()).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            leader = myLeader,
            roles = members.Select(member => new
            {
                role = member.Id,
                status = member.Status,
                lastEventAt = member.LastEventAt,
                error = member.Error,
                activeTool = member.ActiveTool,
                isWorking = member.IsWorking,
                model = member.Model,
                effort = member.Effort,
                aicUsed = member.AicUsed,
                contextUsedTokens = member.ContextUsedTokens,
                contextLimitTokens = member.ContextLimitTokens,
                eventCount = member.EventCount,
            }),
            permissions = members.SelectMany(member => member.Permissions).Select(permission => new
            {
                requestId = permission.RequestId,
                role = permission.Role,
                description = permission.Description,
            }),
            inputs = members.SelectMany(member => member.Inputs).Select(input => new
            {
                requestId = input.RequestId,
                role = input.Role,
                prompt = input.Prompt,
                choices = input.Choices,
                allowFreeform = input.AllowFreeform,
            }),
            elicitations = members.SelectMany(member => member.Elicitations).Select(elicitation => new
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
        return myMemberOrder
            .Select(id => myMembers[id])
            .Select(member => member.Transcript.CreateTranscriptSnapshot(maxEntriesPerRole))
            .ToArray();
    }

    public RoleTranscriptPage CreateTranscriptPage(string role, int beforeIndex, int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(beforeIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        if (!myMembers.TryGetValue(role, out var member))
        {
            throw new InvalidOperationException($"Unknown role: {role}");
        }
        return member.Transcript.CreateTranscriptPage(beforeIndex, maxEntries);
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(string role, int entryIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        if (!myMembers.TryGetValue(role, out var member))
        {
            throw new InvalidOperationException($"Unknown role: {role}");
        }
        return member.Transcript.CreateArchivedTranscriptEntry(entryIndex);
    }

    public AgentElicitationRequest GetPendingElicitation(string role, string requestId) =>
        GetMember(role).GetElicitation(requestId);

    /// <summary>
    /// Returns <see langword="null"/> for an unknown role, <see langword="false"/> when work is inadmissible, and
    /// otherwise the readiness inferred from serialized local state.
    /// </summary>
    private bool? GetRoleReadiness(string role)
    {
        if (!myMembers.TryGetValue(role, out var member))
        {
            return null;
        }
        if (!IsAccepting)
        {
            return false;
        }
        if (member.IsInvalidated)
        {
            return false;
        }
        lock (member.SyncRoot)
            return member.Status == "idle" && !member.IsWorking;
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
            if (myMembers.TryGetValue(session.Role, out var member))
            {
                member.Session = session;
            }
    }

    public Task MarkRoleFailedAsync(string role, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return RouteIgnoringUnknownRoleAsync(role, processor => processor.MarkFailedAsync(exception));
    }

    private void BeginStopping()
    {
        lock (myAdmissionLock)
            myAccepting = false;
        myShutdown.Cancel();
    }

    public Task SendAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        RouteAsync(role, processor => processor.SendPromptAsync((session, token) => session.SendAsync(prompt, token), cancellationToken));

    public Task SendHarnessAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        RouteAsync(role, processor => processor.SendPromptAsync((session, token) => session.SendHarnessAsync(prompt, token), cancellationToken));

    /// <summary>
    /// Coalesces concurrent aborts for a role, cancels its active local operation, and waits for the provider abort.
    /// Events remain invalidated after a failed abort until a later abort succeeds.
    /// </summary>
    public Task AbortAsync(string role, CancellationToken cancellationToken = default) =>
        AbortRoleAndWaitAsync(role, cancellationToken);

    public Task CompletePermissionAsync(string role, string requestId, bool approved, CancellationToken cancellationToken = default) =>
        RouteAsync(role, processor => processor.CompletePermissionAsync(requestId, new AgentPermissionResponse(approved), cancellationToken));

    public Task CompleteInputAsync(string role, string requestId, string? answer, bool wasFreeform, CancellationToken cancellationToken = default) =>
        RouteAsync(role, processor => processor.CompleteInputAsync(requestId, new AgentInputResponse(answer, wasFreeform), cancellationToken));

    public Task CompleteElicitationAsync(string role, string requestId, string action, JsonElement? content, CancellationToken cancellationToken = default) =>
        RouteAsync(role, processor => processor.CompleteElicitationAsync(requestId, new AgentElicitationResponse(action, content), cancellationToken));

    public Task EnqueueEventAsync(string role, AgentEvent agentEvent, CancellationToken cancellationToken = default) =>
        RouteIgnoringUnknownRoleAsync(role, processor => processor.ApplyEventAsync(agentEvent));

    /// <summary>
    /// Stops accepting work, cancels pending provider interactions, and waits for every member's processor to drain
    /// its queued messages and any in-flight detached operation. Repeated calls are safe.
    /// </summary>
    public async Task StopAsync()
    {
        BeginStopping();
        await CancelAllPendingInteractionsAsync();
        await Task.WhenAll(myProcessors.Values.Select(processor => processor.RetireAsync()));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            myShutdown.Dispose();
            foreach (var member in myMembers.Values)
            {
                member.Dispose();
            }
            myTranscriptArchive.Dispose();
        }
    }

    /// <summary>
    /// Rejects a new command once shutdown has begun, then routes it to the named member's processor. The same
    /// rejection - and the same "Unknown role" failure for a role that was never configured - reaches the caller as
    /// a faulted task rather than a synchronous throw, matching every other public command entry point.
    /// </summary>
    private async Task RouteAsync(string role, Func<MemberProcessor, Task> action)
    {
        EnsureAccepting();
        await action(GetProcessor(role));
    }

    /// <summary>
    /// Rejects a new command once shutdown has begun, then routes it to the named member's processor - or silently
    /// completes for a role that was never configured, matching this command's original no-op behavior for
    /// provider events and failures the projector could reasonably see for a role it does not recognize.
    /// </summary>
    private async Task RouteIgnoringUnknownRoleAsync(string role, Func<MemberProcessor, Task> action)
    {
        EnsureAccepting();
        if (myProcessors.TryGetValue(role, out var processor))
        {
            await action(processor);
        }
    }

    private MemberProcessor GetProcessor(string role)
    {
        if (myProcessors.TryGetValue(role, out var processor))
        {
            return processor;
        }
        throw new InvalidOperationException($"Unknown role: {role}");
    }

    private void MarkRoleFailedCore(string role, Exception exception)
    {
        if (!myMembers.TryGetValue(role, out var member))
        {
            return;
        }
        member.MarkFailed();
        RemovePendingInteractionsForMember(member);
        lock (member.SyncRoot)
        {
            member.Status = "error";
            member.Error = exception.Message;
            member.IsWorking = false;
            member.ActiveTool = null;
        }
        NotifyStateChanged();
    }

    private void ApplyProjectedEvent(string role, AgentEvent agentEvent)
    {
        if (!myMembers.TryGetValue(role, out var member))
        {
            return;
        }
        if (member.IsFailed)
        {
            return;
        }
        if (ShouldIgnoreEvent(member, agentEvent))
        {
            return;
        }
        TranscriptUpdate? transcriptUpdate;
        lock (member.SyncRoot)
        {
            transcriptUpdate = MemberEventProjector.Project(member, agentEvent);
            if (transcriptUpdate is not null)
            {
                TranscriptChanged?.Invoke(transcriptUpdate);
            }
        }
        NotifyStateChanged(IsImmediateUiEvent(agentEvent));
    }

    private async Task DispatchPromptAsync(string role, Func<IAgentSession, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        var member = GetMember(role);
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdown.Token);
        using var promptLease = await member.AcquirePromptLeaseAsync(lifetimeCancellation.Token);
        EnsureRoleAvailable(role);
        await member.WaitForAbortAsync(lifetimeCancellation.Token);
        member.ResumeEvents();
        MarkWaitingForResponse(role);
        await RunForRoleAsync(role, operation, lifetimeCancellation.Token);
    }

    private void MarkWaitingForResponse(string role)
    {
        if (myMembers.TryGetValue(role, out var member))
        {
            lock (member.SyncRoot)
            {
                member.IsWorking = true;
                member.ActiveTool = null;
            }
            NotifyStateChanged();
        }
    }

    private async Task RunForRoleAsync(
        string role,
        Func<IAgentSession, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        EnsureAccepting();
        var member = GetMember(role);
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdown.Token);
        if (!TryCaptureSession(role, out var session))
        {
            throw new InvalidOperationException($"Unknown role: {role}");
        }
        using var operationLease = await member.AcquireOperationLeaseAsync(lifetimeCancellation.Token);
        EnsureAccepting();
        EnsureRoleAvailable(role);
        operationLease.Register(lifetimeCancellation);
        await operation(session, lifetimeCancellation.Token);
    }

    /// <summary>
    /// Atomically checks admission and captures the role's current non-terminal session under
    /// <see cref="myAdmissionLock"/> - the same synchronization boundary <see cref="RouteAsync"/> and
    /// <see cref="RegisterSession"/> use, so a stopping transition can never interleave with session selection.
    /// </summary>
    private bool TryCaptureSession(string role, out IAgentSession session)
    {
        lock (myAdmissionLock)
        {
            if (myAccepting
                && myMembers.TryGetValue(role, out var member)
                && member.Session is { } candidate
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
        var member = GetMember(role);
        if (!member.IsFailed)
        {
            return;
        }
        lock (member.SyncRoot)
            throw new InvalidOperationException($"Role '{role}' is unavailable: {member.Error}");
    }

    private async Task AbortRoleAndWaitAsync(string role, CancellationToken cancellationToken)
    {
        var member = GetMember(role);
        var lease = member.TryBeginAbort(out var existingAbort);
        if (lease is null)
        {
            await existingAbort!.WaitAsync(cancellationToken);
            return;
        }

        using (lease)
        {
            try
            {
                await RouteAsync(role, processor => processor.AbortAsync(cancellationToken));
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
            RemovePendingInteractionsForMember(GetMember(role));
            MarkRoleIdle(role);
            NotifyStateChanged();
        }
    }

    private bool ShouldIgnoreEvent(MemberAggregate member, AgentEvent agentEvent)
    {
        if (agentEvent is AgentStartedEvent or AgentStoppedEvent or AgentSessionConfigurationEvent or AgentSessionModelChangedEvent or AgentContextUsageEvent or AgentSessionUsageEvent)
        {
            return false;
        }
        return member.IsInvalidated;
    }

    private void MarkRoleIdle(string role)
    {
        if (!myMembers.TryGetValue(role, out var member))
        {
            return;
        }
        lock (member.SyncRoot)
        {
            member.IsWorking = false;
            member.ActiveTool = null;
        }
    }

    private async Task CompleteInteractionCoreAsync<TRequest>(
        string expectedRole,
        string requestId,
        Func<MemberAggregate, TRequest> remove,
        Action<MemberAggregate, TRequest> restore,
        Func<IAgentSession, CancellationToken, Task> respond,
        Action<string>? onCompleted,
        CancellationToken cancellationToken)
    {
        var member = GetMember(expectedRole);
        var request = remove(member);
        try
        {
            await RunForRoleAsync(expectedRole, respond, cancellationToken);
            onCompleted?.Invoke(expectedRole);
            UnprotectPendingTranscriptEntry(member, requestId);
            NotifyStateChanged();
        }
        catch
        {
            if (!member.IsFailed)
            {
                restore(member, request);
            }
            else
            {
                UnprotectPendingTranscriptEntry(member, requestId);
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
        if (response.Answer is null || !myMembers.TryGetValue(role, out var member))
        {
            return;
        }
        lock (member.SyncRoot)
        {
            var transcriptUpdate = member.Transcript.AddTranscriptEntry(new TranscriptEntry(DateTimeOffset.UtcNow, "user", response.Answer));
            TranscriptChanged?.Invoke(transcriptUpdate);
        }
    }

    private async Task CancelAllPendingInteractionsAsync()
    {
        IAgentSession[] sessions;
        lock (myAdmissionLock)
            sessions = myMembers.Values
                .Select(member => member.Session)
                .Where(session => session is not null)
                .Select(session => session!)
                .ToArray();
        foreach (var session in sessions)
        {
            if (!session.Completion.IsCompleted)
            {
                await session.CancelPendingInteractionsAsync();
            }
        }
        foreach (var member in myMembers.Values)
        {
            member.ClearInteractions();
        }
        NotifyStateChanged();
    }

    private void RemovePendingInteractionsForMember(MemberAggregate member)
    {
        foreach (var entryIndex in member.RemoveAllInteractions())
        {
            lock (member.SyncRoot)
                member.Transcript.UnprotectTranscriptEntry(entryIndex);
        }
    }

    private void UnprotectPendingTranscriptEntry(MemberAggregate member, string requestId)
    {
        var entryIndex = member.TryRemoveProtectedTranscriptEntry(requestId);
        if (entryIndex is null)
        {
            return;
        }
        lock (member.SyncRoot)
            member.Transcript.UnprotectTranscriptEntry(entryIndex.Value);
    }

    private MemberAggregate GetMember(string role)
    {
        if (myMembers.TryGetValue(role, out var member))
        {
            return member;
        }
        throw new InvalidOperationException($"Unknown role: {role}");
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
