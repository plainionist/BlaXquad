using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application.Transcripts;
using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Application;

/// <summary>
/// Owns every transient state transition for one squad member within its squad generation: projected agent status,
/// its provider-session association, transcript, pending interactions, operation admission/cancellation, abort
/// coordination, and terminal failure. It is the sole mutable owner of that state, and every local collection below
/// is keyed only by request or operation identity - never by another member or role.
/// </summary>
internal sealed class SquadMember : IDisposable
{
    private readonly object myStateLock = new();
    private readonly SquadMemberTranscriptState myTranscript;

    private readonly object myInteractionsLock = new();
    private readonly Dictionary<InteractionRequestId, MemberInteractionState> myInteractions = [];

    private readonly SemaphoreSlim myPromptLock = new(1, 1);
    private readonly SemaphoreSlim myOperationLock = new(1, 1);
    private readonly object myOperationStateLock = new();
    private CancellationTokenSource? myActiveOperation;
    private TaskCompletionSource? myAbort;
    private bool myInvalidated;
    private bool myFailedAbort;
    private bool myFailed;

    internal SquadMember(
        SquadGenerationId generation,
        SquadMemberId id,
        string displayName,
        SquadMemberTranscriptArchive transcriptArchive)
    {
        Generation = generation;
        Id = id;
        DisplayName = displayName;
        myTranscript = new SquadMemberTranscriptState(id, transcriptArchive, myStateLock);
    }

    /// <summary>The identity of the squad generation this member belongs to. It never outlives that generation.</summary>
    public SquadGenerationId Generation { get; }
    /// <summary>The member's unique identity, addressed as "role" at unchanged public boundaries.</summary>
    public SquadMemberId Id { get; }
    /// <summary>The member's configured presentation name.</summary>
    public string DisplayName { get; }
    public SquadMemberStatus Status { get; internal set; } = SquadMemberStatus.Starting;
    public DateTimeOffset? LastEventAt { get; internal set; }
    public string? Error { get; internal set; }
    public string? ActiveTool { get; internal set; }
    public bool IsWorking { get; internal set; }
    public string? Model { get; internal set; }
    public string? Effort { get; internal set; }
    public decimal? AicUsed { get; internal set; }
    public long? ContextUsedTokens { get; internal set; }
    public long? ContextLimitTokens { get; internal set; }
    public int EventCount { get; internal set; }

    /// <summary>
    /// This member's current provider session, reached only through this aggregate. Set once under the squad
    /// facade's admission lock and read back under that same lock so a stopping transition and a session capture
    /// can never interleave.
    /// </summary>
    internal IAgentSession? Session { get; set; }

    internal object SyncRoot => myStateLock;
    internal SquadMemberTranscriptState Transcript => myTranscript;

    internal SquadMemberSnapshot CreateSnapshot()
    {
        lock (myStateLock)
        {
            return new SquadMemberSnapshot(
                Id,
                DisplayName,
                Status,
                LastEventAt,
                Error,
                ActiveTool,
                IsWorking,
                Model,
                Effort,
                AicUsed,
                ContextUsedTokens,
                ContextLimitTokens,
                EventCount,
                myTranscript.TranscriptSequence,
                Permissions,
                Inputs,
                Elicitations);
        }
    }

    #region Pending interactions

    internal IReadOnlyCollection<AgentPermissionRequest> Permissions
    {
        get { lock (myInteractionsLock) return PendingRequests<AgentPermissionRequest>(); }
    }

    internal IReadOnlyCollection<AgentInputRequest> Inputs
    {
        get { lock (myInteractionsLock) return PendingRequests<AgentInputRequest>(); }
    }

    internal IReadOnlyCollection<AgentElicitationRequest> Elicitations
    {
        get { lock (myInteractionsLock) return PendingRequests<AgentElicitationRequest>(); }
    }

    internal AgentElicitationRequest GetElicitation(InteractionRequestId requestId)
    {
        lock (myInteractionsLock)
        {

            if (myInteractions.TryGetValue(requestId, out var state) &&
                state is MemberInteractionState.Pending<AgentElicitationRequest> pending)
            {

                return pending.Request;
            }

            throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{Id}'.");
        }
    }

    /// <summary>
    /// Registers a fresh permission request, appends and protects its transcript entry, and stores the complete
    /// interaction state as one owner-level transition. Validates request-id uniqueness across every request kind,
    /// not merely within this one.
    /// </summary>
    internal TranscriptUpdate RegisterPermission(AgentPermissionRequest request, TranscriptEntry entry) =>
        RegisterInteraction(request.RequestId, request, entry);

    internal TranscriptUpdate RegisterInput(AgentInputRequest request, TranscriptEntry entry) =>
        RegisterInteraction(request.RequestId, request, entry);

    internal TranscriptUpdate RegisterElicitation(AgentElicitationRequest request, TranscriptEntry entry) =>
        RegisterInteraction(request.RequestId, request, entry);

    /// <summary>
    /// Transitions a pending interaction of the expected request kind to responding, so it survives while provider
    /// I/O is in flight but no longer appears in a pending-interaction snapshot.
    /// </summary>
    internal void BeginResponding<TRequest>(InteractionRequestId requestId)
    {
        lock (myInteractionsLock)
        {

            if (myInteractions.TryGetValue(requestId, out var state) &&
                state is MemberInteractionState.Pending<TRequest> pending)
            {

                myInteractions[requestId] = new MemberInteractionState.Responding<TRequest>(pending.Request, pending.ProtectedTranscriptEntryIndex);
                return;
            }

            throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{Id}'.");
        }
    }

    /// <summary>
    /// Transitions a responding interaction back to pending after a recoverable provider failure. A no-op when the
    /// interaction was concurrently removed by an abort, or retained-not-responding by a headquarters shutdown that
    /// raced this same response - either way there is nothing left to restore, and this must never throw, since it
    /// runs as a read-loop mutation with no surrounding catch.
    /// </summary>
    internal void RestorePending(InteractionRequestId requestId)
    {
        lock (myInteractionsLock)
        {

            if (myInteractions.TryGetValue(requestId, out var state) && state.TryRestore(out var restored))
            {
                myInteractions[requestId] = restored;
            }

        }
    }

    /// <summary>Removes one interaction regardless of its phase and returns the transcript entry it protected.</summary>
    internal int? TryRemoveProtectedTranscriptEntry(InteractionRequestId requestId)
    {
        lock (myInteractionsLock)
            return myInteractions.Remove(requestId, out var state) ? state.ProtectedTranscriptEntryIndex : null;
    }

    /// <summary>Removes every interaction for this member and returns the transcript entries they protected.</summary>
    internal IReadOnlyList<int> RemoveAllInteractions()
    {
        lock (myInteractionsLock)
        {
            var protectedEntries = myInteractions.Values.Select(state => state.ProtectedTranscriptEntryIndex).ToArray();
            myInteractions.Clear();
            return protectedEntries;
        }
    }

    /// <summary>
    /// Clears every request kind from the published pending-interaction view for headquarters shutdown, while
    /// deliberately leaving each entry's transcript protection in place for the rest of the generation's retirement.
    /// </summary>
    internal void ClearInteractions()
    {
        lock (myInteractionsLock)
        {

            foreach (var (requestId, state) in myInteractions.ToArray())
            {
                myInteractions[requestId] = new MemberInteractionState.RetainedForRetirement(state.ProtectedTranscriptEntryIndex);
            }

        }
    }

    private TranscriptUpdate RegisterInteraction<TRequest>(InteractionRequestId requestId, TRequest request, TranscriptEntry entry)
    {
        lock (myInteractionsLock)
        {

            if (myInteractions.ContainsKey(requestId))
            {
                throw new InvalidOperationException($"Interaction '{requestId}' is already pending for role '{Id}'.");
            }

            var update = myTranscript.AddTranscriptEntry(entry, protect: true);
            myInteractions.Add(requestId, new MemberInteractionState.Pending<TRequest>(request, update.EntryIndex));
            return update;
        }
    }

    private TRequest[] PendingRequests<TRequest>() =>
        myInteractions.Values
            .OfType<MemberInteractionState.Pending<TRequest>>()
            .Select(state => state.Request)
            .ToArray();

    #endregion

    #region Operation coordination

    internal bool IsFailed
    {
        get { lock (myOperationStateLock) return myFailed; }
    }

    /// <summary>Marks this member permanently failed and cancels its active operation, if any.</summary>
    internal void MarkFailed()
    {
        lock (myOperationStateLock)
        {
            myFailed = true;
            CancelActiveOperationLocked();
        }
    }

    internal bool IsInvalidated
    {
        get { lock (myOperationStateLock) return myInvalidated; }
    }

    /// <summary>Resumes event admission for this member after a prompt waits out its abort.</summary>
    internal void ResumeEvents()
    {
        lock (myOperationStateLock)
            myInvalidated = false;
    }

    /// <summary>Acquires this member's prompt serialization slot. Prompts for the same member wait for one another.</summary>
    internal async Task<PromptLease> AcquirePromptLeaseAsync(CancellationToken cancellationToken)
    {
        await myPromptLock.WaitAsync(cancellationToken);
        return new PromptLease(myPromptLock);
    }

    /// <summary>
    /// Acquires this member's operation serialization slot. Callers must call <see cref="OperationLease.Register"/>
    /// once admission checks pass, then dispose the lease when the operation completes.
    /// </summary>
    internal async Task<OperationLease> AcquireOperationLeaseAsync(CancellationToken cancellationToken)
    {
        await myOperationLock.WaitAsync(cancellationToken);
        return new OperationLease(this, myOperationLock);
    }

    /// <summary>Waits for this member's in-flight abort, if any, or fails immediately if a prior abort left it closed.</summary>
    internal Task WaitForAbortAsync(CancellationToken cancellationToken)
    {
        Task? abort;
        bool abortFailed;
        lock (myOperationStateLock)
        {
            abort = myAbort?.Task;
            abortFailed = myFailedAbort;
        }

        return abort?.WaitAsync(cancellationToken) ??
            (abortFailed
                ? Task.FromException(new InvalidOperationException($"Role '{Id}' remains cancelled because its abort failed."))
                : Task.CompletedTask);
    }

    /// <summary>
    /// Begins an abort for this member. Returns the leader's lease, having invalidated event admission and
    /// cancelled the active local operation atomically, or returns <see langword="null"/> with the in-flight abort
    /// task when a concurrent abort is already underway for the member.
    /// </summary>
    internal AbortLease? TryBeginAbort(out Task? existingAbort)
    {
        lock (myOperationStateLock)
        {

            if (myAbort is not null)
            {
                existingAbort = myAbort.Task;
                return null;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            myAbort = completion;
            myInvalidated = true;
            CancelActiveOperationLocked();
            existingAbort = null;
            return new AbortLease(this, completion);
        }
    }

    public void Dispose()
    {
        myPromptLock.Dispose();
        myOperationLock.Dispose();
    }

    internal void RegisterOperation(CancellationTokenSource operation)
    {
        lock (myOperationStateLock)
        {
            myActiveOperation = operation;

            if (myInvalidated)
            {
                operation.Cancel();
            }

        }
    }

    internal void UnregisterOperation(CancellationTokenSource operation)
    {
        lock (myOperationStateLock)

            if (ReferenceEquals(myActiveOperation, operation))
            {
                myActiveOperation = null;
            }
    }

    internal void RemoveAbort()
    {
        lock (myOperationStateLock)
            myAbort = null;
    }

    internal void ClearFailedAbort()
    {
        lock (myOperationStateLock)
            myFailedAbort = false;
    }

    internal void MarkFailedAbort()
    {
        lock (myOperationStateLock)
            myFailedAbort = true;
    }

    private void CancelActiveOperationLocked()
    {
        myActiveOperation?.Cancel();
    }

    #endregion
}
