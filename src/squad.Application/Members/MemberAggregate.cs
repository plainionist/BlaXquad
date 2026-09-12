using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application.Transcripts;
using squad.Domain;

namespace squad.Application.Members;

/// <summary>
/// Owns every transient state transition for one squad member within its squad generation: projected agent status,
/// its provider-session association, transcript, pending interactions, operation admission/cancellation, abort
/// coordination, and terminal failure. It is the sole mutable owner of that state, and every local collection below
/// is keyed only by request or operation identity - never by another member or role.
/// </summary>
internal sealed class MemberAggregate : IDisposable
{
    private readonly object myStateLock = new();
    private readonly MemberTranscriptState myTranscript;

    private readonly object myInteractionsLock = new();
    private readonly Dictionary<string, AgentPermissionRequest> myPermissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentInputRequest> myInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentElicitationRequest> myElicitations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> myProtectedTranscriptEntries = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim myPromptLock = new(1, 1);
    private readonly SemaphoreSlim myOperationLock = new(1, 1);
    private readonly object myOperationStateLock = new();
    private CancellationTokenSource? myActiveOperation;
    private TaskCompletionSource? myAbort;
    private bool myInvalidated;
    private bool myFailedAbort;
    private bool myFailed;

    internal MemberAggregate(
        SquadGenerationId generation,
        SquadMemberId id,
        string displayName,
        RoleId role,
        MemberTranscriptArchive transcriptArchive)
    {
        Generation = generation;
        Id = id;
        DisplayName = displayName;
        Role = role;
        myTranscript = new MemberTranscriptState(id.Value, transcriptArchive, myStateLock);
    }

    /// <summary>The identity of the squad generation this member belongs to. It never outlives that generation.</summary>
    public SquadGenerationId Generation { get; }
    /// <summary>The member's unique identity, addressed as "role" at unchanged public boundaries.</summary>
    public SquadMemberId Id { get; }
    /// <summary>The member's configured presentation name.</summary>
    public string DisplayName { get; }
    /// <summary>The role this member references. Distinct from <see cref="Id"/>: multiple members may share one role.</summary>
    public RoleId Role { get; }
    public string Status { get; internal set; } = "starting";
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
    internal MemberTranscriptState Transcript => myTranscript;

    internal MemberSnapshot CreateSnapshot()
    {
        lock (myStateLock)
        {
            return new MemberSnapshot(
                Id.Value,
                DisplayName,
                Role.Value,
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
        get { lock (myInteractionsLock) return myPermissions.Values.ToArray(); }
    }

    internal IReadOnlyCollection<AgentInputRequest> Inputs
    {
        get { lock (myInteractionsLock) return myInputs.Values.ToArray(); }
    }

    internal IReadOnlyCollection<AgentElicitationRequest> Elicitations
    {
        get { lock (myInteractionsLock) return myElicitations.Values.ToArray(); }
    }

    internal AgentElicitationRequest GetElicitation(string requestId)
    {
        lock (myInteractionsLock)
        {
            if (myElicitations.TryGetValue(requestId, out var request))
            {
                return request;
            }
            throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{Id}'.");
        }
    }

    internal void RegisterPermission(AgentPermissionRequest request) => Register(myPermissions, request.RequestId, request);

    internal void RegisterInput(AgentInputRequest request) => Register(myInputs, request.RequestId, request);

    internal void RegisterElicitation(AgentElicitationRequest request) => Register(myElicitations, request.RequestId, request);

    internal void ProtectTranscriptEntry(string requestId, int entryIndex)
    {
        lock (myInteractionsLock)
            myProtectedTranscriptEntries[requestId] = entryIndex;
    }

    internal AgentPermissionRequest RemovePermission(string requestId) => Remove(myPermissions, requestId);

    internal AgentInputRequest RemoveInput(string requestId) => Remove(myInputs, requestId);

    internal AgentElicitationRequest RemoveElicitation(string requestId) => Remove(myElicitations, requestId);

    internal int? TryRemoveProtectedTranscriptEntry(string requestId)
    {
        lock (myInteractionsLock)
            return myProtectedTranscriptEntries.Remove(requestId, out var entryIndex) ? entryIndex : null;
    }

    /// <summary>Removes every pending interaction for this member and returns the transcript entries they protected.</summary>
    internal IReadOnlyList<int> RemoveAllInteractions()
    {
        lock (myInteractionsLock)
        {
            myPermissions.Clear();
            myInputs.Clear();
            myElicitations.Clear();
            var protectedEntries = myProtectedTranscriptEntries.Values.ToArray();
            myProtectedTranscriptEntries.Clear();
            return protectedEntries;
        }
    }

    internal void ClearInteractions()
    {
        lock (myInteractionsLock)
        {
            myPermissions.Clear();
            myInputs.Clear();
            myElicitations.Clear();
        }
    }

    private void Register<TRequest>(Dictionary<string, TRequest> requests, string requestId, TRequest request)
    {
        lock (myInteractionsLock)
        {
            if (!requests.TryAdd(requestId, request))
            {
                throw new InvalidOperationException($"Interaction '{requestId}' is already pending for role '{Id}'.");
            }
        }
    }

    private TRequest Remove<TRequest>(Dictionary<string, TRequest> requests, string requestId)
    {
        lock (myInteractionsLock)
        {
            if (requests.Remove(requestId, out var request))
            {
                return request;
            }
            throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{Id}'.");
        }
    }

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
