using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application.Members;
using squad.Application.Transcripts;
using squad.Domain;
using squad.Ui.Abstractions;
using System.Diagnostics;
using System.Text.Json;

namespace squad.Application;

/// <summary>
/// One squad generation's ordered member directory, command admission, and read-model composition. It is created
/// with its generation identity and configuration snapshot, owns every member processor and all transient member
/// state, and is reached only through the <see cref="Squad"/>-equivalent owner that installs it: nothing outside
/// this object holds a member processor or aggregate.
/// </summary>
public sealed class SquadMembers : IDisposable
{
    private readonly CancellationTokenSource myShutdown = new();
    // The ordered member directory is the only application-domain collection keyed by member identity. Each
    // member's processor is its aggregate's sole mutable accessor - the sole path through which a prompt, harness,
    // abort, interaction-response, provider-event, or session-terminal message reaches that member's MemberAggregate,
    // reached here only through processor.Aggregate for read-only snapshot and query composition. A slow or blocked
    // provider call for one member can never delay another member's processor, and never delays this member's own
    // provider-event or session-terminal messages either, since those are applied inline without awaiting provider
    // I/O.
    private readonly Dictionary<SquadMemberId, MemberProcessor> myMembers = new();
    private readonly List<SquadMemberId> myMemberOrder = [];
    private readonly SquadMemberId myLeader;
    private readonly GenerationTranscriptArchive myTranscripts;
    private readonly ISquadPublication myPublication;
    // The one synchronization boundary for command admission and active-session selection: myAccepting and each
    // member's Session are read and written only while holding this lock, so a stopping transition and a session
    // capture can never interleave.
    private readonly object myAdmissionLock = new();
    private bool myAccepting = true;
    private volatile bool myRetired;

    public SquadMembers(
        SquadGenerationId generation,
        SquadDefinition definition,
        TranscriptStore transcripts,
        ISquadPublication publication)
    {
        Generation = generation;
        myLeader = definition.Leader;
        myPublication = publication;
        myTranscripts = transcripts.OpenGeneration(generation);
        foreach (var member in definition.Members)
        {
            if (myMembers.ContainsKey(member.Id))
            {
                continue;
            }
            var aggregate = new MemberAggregate(
                generation,
                member.Id,
                member.DisplayName,
                myTranscripts.OpenMember(member.Id));
            myMembers.Add(member.Id, new MemberProcessor(
                aggregate,
                myAdmissionLock,
                isAcceptingUnlocked: () => myAccepting,
                myShutdown.Token,
                notifyStateChanged: NotifyStateChanged,
                transcriptChanged: PublishTranscriptUpdate));
            myMemberOrder.Add(member.Id);
        }
        Contract.Invariant(myMemberOrder.Count == myMembers.Count, "Member order must track every configured member exactly once.");
        Contract.Invariant(myMembers.ContainsKey(myLeader), "The leader must be one of this generation's configured members.");
    }

    /// <summary>This generation's strong identity. Every member processor and archive handle is bound to it.</summary>
    public SquadGenerationId Generation { get; }

    /// <summary>Whether this generation has been retired and can no longer publish or admit work.</summary>
    public bool IsRetired => myRetired;

    public JsonElement CreateSnapshot()
    {
        // Enumerate in configured member order (myMemberOrder), not myMembers.Values, so state.snapshot.roles
        // matches blaxquad/squad.json regardless of Dictionary enumeration behavior.
        var members = myMemberOrder.Select(id => myMembers[id].Aggregate.CreateSnapshot()).ToArray();
        return CreateSnapshot(myLeader.Value, members);
    }

    /// <summary>The read model published while no squad generation is installed: a leaderless, memberless squad.</summary>
    internal static JsonElement CreateEmptySnapshot() => CreateSnapshot("", []);

    public IReadOnlyList<RoleTranscriptSnapshot> CreateTranscriptSnapshot(int maxEntriesPerRole)
    {
        Contract.Requires(maxEntriesPerRole > 0, "maxEntriesPerRole must be positive.");
        return myMemberOrder
            .Select(id => myMembers[id].Aggregate)
            .Select(member => member.Transcript.CreateTranscriptSnapshot(maxEntriesPerRole))
            .ToArray();
    }

    public RoleTranscriptPage CreateTranscriptPage(SquadMemberId memberId, int beforeIndex, int maxEntries)
    {
        Contract.Requires(beforeIndex >= 0, "beforeIndex must not be negative.");
        Contract.Requires(maxEntries > 0, "maxEntries must be positive.");
        return GetMember(memberId).Transcript.CreateTranscriptPage(beforeIndex, maxEntries);
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(SquadMemberId memberId, int entryIndex)
    {
        Contract.Requires(entryIndex >= 0, "entryIndex must not be negative.");
        return GetMember(memberId).Transcript.CreateArchivedTranscriptEntry(entryIndex);
    }

    public AgentElicitationRequest GetPendingElicitation(SquadMemberId memberId, InteractionRequestId requestId) =>
        GetMember(memberId).GetElicitation(requestId);

    /// <summary>
    /// Returns <see langword="null"/> for an unknown role, <see langword="false"/> when work is inadmissible, and
    /// otherwise the readiness inferred from serialized local state.
    /// </summary>
    public bool? GetRoleReadiness(SquadMemberId memberId)
    {
        if (!myMembers.TryGetValue(memberId, out var processor))
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
            return member.Status == SquadMemberStatus.Idle && !member.IsWorking;
    }

    /// <summary>
    /// Sets a role's active provider session by routing to that member's processor. Unlike command routing, an
    /// unrecognized role here signals a broken provider callback - every session role is drawn from this
    /// generation's own configured roster - so it is rejected rather than silently ignored.
    /// </summary>
    public void RegisterSession(IAgentSession session)
    {
        var isKnownMember = myMembers.TryGetValue(session.MemberId, out var processor);
        Contract.Requires(isKnownMember, $"Unknown role: {session.MemberId}");
        processor!.SetSession(session);
    }

    public Task MarkRoleFailedAsync(SquadMemberId memberId, Exception exception)
    {
        return RouteIgnoringUnknownRoleAsync(memberId, processor => processor.MarkFailedAsync(exception));
    }

    public Task SendAsync(SquadMemberId memberId, string prompt, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, processor => processor.SendPromptAsync(prompt, cancellationToken));

    public Task SendHarnessAsync(SquadMemberId memberId, string prompt, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, processor => processor.SendHarnessAsync(prompt, cancellationToken));

    /// <summary>
    /// Coalesces concurrent aborts for a role, cancels its active local operation, and waits for the provider abort.
    /// Events remain invalidated after a failed abort until a later abort succeeds.
    /// </summary>
    public Task AbortAsync(SquadMemberId memberId, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, processor => processor.AbortAsync(cancellationToken));

    public Task CompletePermissionAsync(SquadMemberId memberId, InteractionRequestId requestId, bool approved, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, processor => processor.CompletePermissionAsync(requestId, new AgentPermissionResponse(approved), cancellationToken));

    public Task CompleteInputAsync(SquadMemberId memberId, InteractionRequestId requestId, string? answer, bool wasFreeform, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, processor => processor.CompleteInputAsync(requestId, new AgentInputResponse(answer, wasFreeform), cancellationToken));

    public Task CompleteElicitationAsync(SquadMemberId memberId, InteractionRequestId requestId, ElicitationAction action, JsonElement? content, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, processor => processor.CompleteElicitationAsync(requestId, new AgentElicitationResponse(action, content), cancellationToken));

    public Task EnqueueEventAsync(SquadMemberId memberId, AgentEvent agentEvent, CancellationToken cancellationToken = default) =>
        RouteIgnoringUnknownRoleAsync(memberId, processor => processor.ApplyEventAsync(agentEvent));

    /// <summary>
    /// Closes command admission for this generation and cancels every in-flight member operation. The first step of
    /// retirement, taken before handoff participation stops and before any processor is drained, so no further work
    /// can be admitted while the generation winds down. Idempotent.
    /// </summary>
    public void CloseAdmission()
    {
        lock (myAdmissionLock)
        {
            if (!myAccepting)
            {
                return;
            }
            myAccepting = false;
        }
        myShutdown.Cancel();
    }

    /// <summary>
    /// Cancels pending provider interactions and waits for every member's processor to drain its queued messages
    /// and any in-flight detached operation. Repeated calls are safe.
    /// </summary>
    public async Task DrainAsync()
    {
        CloseAdmission();
        await Task.WhenAll(myMembers.Values.Select(processor => processor.CancelPendingInteractionsAsync()));
        await Task.WhenAll(myMembers.Values.Select(processor => processor.RetireAsync()));
    }

    /// <summary>
    /// Marks this generation retired once its processors have drained: no member may publish through the
    /// process-lifetime port again, and the generation's transcript archive handle is revoked so a retired member
    /// cannot write history while every entry it already published remains readable.
    /// </summary>
    public void Retire()
    {
        myRetired = true;
        myTranscripts.Revoke();
    }

    /// <summary>Releases the per-member synchronization primitives once retirement is conclusive.</summary>
    public void Dispose()
    {
        myShutdown.Dispose();
        foreach (var processor in myMembers.Values)
        {
            processor.Dispose();
        }
    }

    private static JsonElement CreateSnapshot(string leader, IReadOnlyList<MemberSnapshot> members) =>
        JsonSerializer.SerializeToElement(new
        {
            leader,
            roles = members.Select(member => new
            {
                role = member.Id.Value,
                status = MapStatus(member.Status),
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
            permissions = members.SelectMany(member => member.Permissions.Select(permission => new
            {
                requestId = permission.RequestId.Value,
                role = member.Id.Value,
                description = permission.Description,
            })),
            inputs = members.SelectMany(member => member.Inputs.Select(input => new
            {
                requestId = input.RequestId.Value,
                role = member.Id.Value,
                prompt = input.Prompt,
                choices = input.Choices,
                allowFreeform = input.AllowFreeform,
            })),
            elicitations = members.SelectMany(member => member.Elicitations.Select(elicitation => new
            {
                requestId = elicitation.RequestId.Value,
                role = member.Id.Value,
                prompt = elicitation.Prompt,
                mode = elicitation.Mode switch
                {
                    ElicitationMode.Form => "form",
                    ElicitationMode.Url => "url",
                    _ => throw new UnreachableException(),
                },
                requestedSchema = elicitation.RequestedSchema,
                url = elicitation.Url,
            })),
        });

    /// <summary>
    /// Maps the internal <see cref="SquadMemberStatus"/> to the stable lowercase spelling every existing
    /// dashboard, transcript, and acceptance scenario observes in <c>state.snapshot</c>. This is the one place
    /// that boundary is crossed; nothing else in this module composes or compares the protocol string.
    /// </summary>
    private static string MapStatus(SquadMemberStatus status) => status switch
    {
        SquadMemberStatus.Starting => "starting",
        SquadMemberStatus.Running => "running",
        SquadMemberStatus.Idle => "idle",
        SquadMemberStatus.Stopped => "stopped",
        SquadMemberStatus.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped squad member status."),
    };

    /// <summary>
    /// Rejects a new command once this generation has closed admission, then routes it to the named member's
    /// processor. The same rejection - and the same "Unknown role" failure for a role that was never configured -
    /// reaches the caller as a faulted task rather than a synchronous throw, matching every other command entry
    /// point.
    /// </summary>
    private async Task RouteAsync(SquadMemberId memberId, Func<MemberProcessor, Task> action)
    {
        EnsureAccepting();
        await action(GetProcessor(memberId));
    }

    /// <summary>
    /// Rejects a new command once this generation has closed admission, then routes it to the named member's
    /// processor - or silently completes for a role that was never configured, matching this command's original
    /// no-op behavior for provider events and failures the projector could reasonably see for a role it does not
    /// recognize.
    /// </summary>
    private async Task RouteIgnoringUnknownRoleAsync(SquadMemberId memberId, Func<MemberProcessor, Task> action)
    {
        EnsureAccepting();
        if (myMembers.TryGetValue(memberId, out var processor))
        {
            await action(processor);
        }
    }

    private MemberProcessor GetProcessor(SquadMemberId memberId)
    {
        if (myMembers.TryGetValue(memberId, out var processor))
        {
            return processor;
        }
        throw new InvalidOperationException($"Unknown role: {memberId}");
    }

    private MemberAggregate GetMember(SquadMemberId memberId) => GetProcessor(memberId).Aggregate;

    private bool IsAccepting
    {
        get { lock (myAdmissionLock) return myAccepting; }
    }

    private void EnsureAccepting()
    {
        if (!IsAccepting)
        {
            throw new OperationCanceledException("Squad is shutting down");
        }
    }

    private void NotifyStateChanged(bool immediate)
    {
        if (myRetired)
        {
            return;
        }
        myPublication.NotifyStateChanged(
            Generation,
            immediate ? UiRefreshPriority.Immediate : UiRefreshPriority.Deferred);
    }

    private void PublishTranscriptUpdate(TranscriptUpdate update)
    {
        if (myRetired)
        {
            return;
        }
        myPublication.PublishTranscriptUpdate(Generation, update);
    }
}
