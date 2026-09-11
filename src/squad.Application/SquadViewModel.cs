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
    // The ordered member directory is the only application-domain collection keyed by member identity. Each
    // member's processor is its aggregate's sole mutable accessor - the sole path through which a prompt, harness,
    // abort, interaction-response, provider-event, or session-terminal message reaches that member's MemberAggregate,
    // reached here only through processor.Aggregate for read-only snapshot and query composition. A slow or blocked
    // provider call for one member can never delay another member's processor, and never delays this member's own
    // provider-event or session-terminal messages either, since those are applied inline without awaiting provider
    // I/O.
    private readonly Dictionary<string, MemberProcessor> myMembers = new(StringComparer.Ordinal);
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
            if (myMembers.ContainsKey(role))
            {
                continue;
            }
            var aggregate = new MemberAggregate(role, configuration.DisplayName, configuration.Role, myTranscriptArchive, myTranscriptRetentionOptions);
            myMembers.Add(role, new MemberProcessor(
                aggregate,
                myAdmissionLock,
                isAcceptingUnlocked: () => myAccepting,
                myShutdown.Token,
                notifyStateChanged: NotifyStateChanged,
                transcriptChanged: transcriptUpdate => TranscriptChanged?.Invoke(transcriptUpdate)));
            myMemberOrder.Add(role);
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
        var members = myMemberOrder.Select(id => myMembers[id].Aggregate.CreateSnapshot()).ToArray();
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
            .Select(id => myMembers[id].Aggregate)
            .Select(member => member.Transcript.CreateTranscriptSnapshot(maxEntriesPerRole))
            .ToArray();
    }

    public RoleTranscriptPage CreateTranscriptPage(string role, int beforeIndex, int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(beforeIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        return GetMember(role).Transcript.CreateTranscriptPage(beforeIndex, maxEntries);
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(string role, int entryIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        return GetMember(role).Transcript.CreateArchivedTranscriptEntry(entryIndex);
    }

    public AgentElicitationRequest GetPendingElicitation(string role, string requestId) =>
        GetMember(role).GetElicitation(requestId);

    /// <summary>
    /// Returns <see langword="null"/> for an unknown role, <see langword="false"/> when work is inadmissible, and
    /// otherwise the readiness inferred from serialized local state.
    /// </summary>
    private bool? GetRoleReadiness(string role)
    {
        if (!myMembers.TryGetValue(role, out var processor))
        {
            return null;
        }
        var member = processor.Aggregate;
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

    /// <summary>Sets a role's active provider session by routing to that member's processor.</summary>
    public void RegisterSession(IAgentSession session)
    {
        if (myMembers.TryGetValue(session.Role, out var processor))
        {
            processor.SetSession(session);
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
        RouteAsync(role, processor => processor.SendPromptAsync(prompt, cancellationToken));

    public Task SendHarnessAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        RouteAsync(role, processor => processor.SendHarnessAsync(prompt, cancellationToken));

    /// <summary>
    /// Coalesces concurrent aborts for a role, cancels its active local operation, and waits for the provider abort.
    /// Events remain invalidated after a failed abort until a later abort succeeds.
    /// </summary>
    public Task AbortAsync(string role, CancellationToken cancellationToken = default) =>
        RouteAsync(role, processor => processor.AbortAsync(cancellationToken));

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
        await Task.WhenAll(myMembers.Values.Select(processor => processor.CancelPendingInteractionsAsync()));
        await Task.WhenAll(myMembers.Values.Select(processor => processor.RetireAsync()));
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
            foreach (var processor in myMembers.Values)
            {
                processor.Dispose();
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
        if (myMembers.TryGetValue(role, out var processor))
        {
            await action(processor);
        }
    }

    private MemberProcessor GetProcessor(string role)
    {
        if (myMembers.TryGetValue(role, out var processor))
        {
            return processor;
        }
        throw new InvalidOperationException($"Unknown role: {role}");
    }

    private MemberAggregate GetMember(string role) => GetProcessor(role).Aggregate;

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
}
