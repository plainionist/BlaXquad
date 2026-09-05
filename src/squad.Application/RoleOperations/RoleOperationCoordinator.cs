namespace squad.Application.RoleOperations;

/// <summary>
/// Owns per-role prompt and operation serialization, active-operation cancellation, invalidation, failed-role
/// state, and abort leader/follower coordination. This is the single synchronization authority for role operation
/// admission; it holds no session, does not invoke provider APIs, does not enqueue commands, and does not mutate
/// role status or transcript state. Callers remain responsible for session lookup/invocation, command admission,
/// and role status/transcript mutation at the existing serialized commit boundary.
/// </summary>
internal sealed class RoleOperationCoordinator : IDisposable
{
    private readonly Dictionary<string, SemaphoreSlim> myRoleLocks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> myPromptLocks = new(StringComparer.Ordinal);
    private readonly object myLock = new();
    private readonly Dictionary<string, CancellationTokenSource> myActiveOperations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource> myAborts = new(StringComparer.Ordinal);
    private readonly HashSet<string> myInvalidatedRoles = new(StringComparer.Ordinal);
    private readonly HashSet<string> myFailedAborts = new(StringComparer.Ordinal);
    private readonly HashSet<string> myFailedRoles = new(StringComparer.Ordinal);

    public bool IsRoleFailed(string role)
    {
        lock (myLock)
            return myFailedRoles.Contains(role);
    }

    /// <summary>Marks a role permanently failed and cancels its active operation, if any.</summary>
    public void MarkRoleFailed(string role)
    {
        lock (myLock)
        {
            myFailedRoles.Add(role);
            CancelActiveOperationLocked(role);
        }
    }

    public bool IsInvalidated(string role)
    {
        lock (myLock)
            return myInvalidatedRoles.Contains(role);
    }

    /// <summary>Resumes event admission for a role after a prompt waits out its abort.</summary>
    public void ResumeEvents(string role)
    {
        lock (myLock)
            myInvalidatedRoles.Remove(role);
    }

    /// <summary>Acquires the role's prompt serialization slot. Prompts for the same role wait for one another.</summary>
    public async Task<RolePromptLease> AcquirePromptLeaseAsync(string role, CancellationToken cancellationToken)
    {
        var promptLock = GetPromptLock(role);
        await promptLock.WaitAsync(cancellationToken);
        return new RolePromptLease(promptLock);
    }

    /// <summary>
    /// Acquires the role's operation serialization slot. Callers must call <see cref="RoleOperationLease.Register"/>
    /// once admission checks pass, then dispose the lease when the operation completes.
    /// </summary>
    public async Task<RoleOperationLease> AcquireOperationLeaseAsync(string role, CancellationToken cancellationToken)
    {
        var roleLock = GetRoleLock(role);
        await roleLock.WaitAsync(cancellationToken);
        return new RoleOperationLease(this, role, roleLock);
    }

    /// <summary>Waits for a role's in-flight abort, if any, or fails immediately if a prior abort left it closed.</summary>
    public Task WaitForAbortAsync(string role, CancellationToken cancellationToken)
    {
        Task? abort;
        bool abortFailed;
        lock (myLock)
        {
            abort = myAborts.GetValueOrDefault(role)?.Task;
            abortFailed = myFailedAborts.Contains(role);
        }
        return abort?.WaitAsync(cancellationToken) ??
            (abortFailed
                ? Task.FromException(new InvalidOperationException($"Role '{role}' remains cancelled because its abort failed."))
                : Task.CompletedTask);
    }

    /// <summary>
    /// Begins a role abort. Returns the leader's lease, having invalidated event admission and cancelled the
    /// active local operation atomically, or returns <see langword="null"/> with the in-flight abort task when a
    /// concurrent abort is already underway for the role.
    /// </summary>
    public RoleAbortLease? TryBeginAbort(string role, out Task? existingAbort)
    {
        lock (myLock)
        {
            if (myAborts.TryGetValue(role, out var existing))
            {
                existingAbort = existing.Task;
                return null;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            myAborts.Add(role, completion);
            myInvalidatedRoles.Add(role);
            CancelActiveOperationLocked(role);
            existingAbort = null;
            return new RoleAbortLease(this, role, completion);
        }
    }

    public void Dispose()
    {
        foreach (var roleLock in myRoleLocks.Values)
            roleLock.Dispose();
        foreach (var promptLock in myPromptLocks.Values)
            promptLock.Dispose();
    }

    internal void RegisterOperation(string role, CancellationTokenSource operation)
    {
        lock (myLock)
        {
            myActiveOperations[role] = operation;
            if (myInvalidatedRoles.Contains(role))
                operation.Cancel();
        }
    }

    internal void UnregisterOperation(string role, CancellationTokenSource operation)
    {
        lock (myLock)
            if (myActiveOperations.TryGetValue(role, out var active) && ReferenceEquals(active, operation))
                myActiveOperations.Remove(role);
    }

    internal void RemoveAbort(string role)
    {
        lock (myLock)
            myAborts.Remove(role);
    }

    internal void ClearFailedAbort(string role)
    {
        lock (myLock)
            myFailedAborts.Remove(role);
    }

    internal void MarkFailedAbort(string role)
    {
        lock (myLock)
            myFailedAborts.Add(role);
    }

    private void CancelActiveOperationLocked(string role)
    {
        if (myActiveOperations.TryGetValue(role, out var operation))
            operation.Cancel();
    }

    private SemaphoreSlim GetRoleLock(string role)
    {
        lock (myRoleLocks)
        {
            if (!myRoleLocks.TryGetValue(role, out var roleLock))
                myRoleLocks[role] = roleLock = new SemaphoreSlim(1, 1);
            return roleLock;
        }
    }

    private SemaphoreSlim GetPromptLock(string role)
    {
        lock (myPromptLocks)
        {
            if (!myPromptLocks.TryGetValue(role, out var promptLock))
                myPromptLocks[role] = promptLock = new SemaphoreSlim(1, 1);
            return promptLock;
        }
    }
}
