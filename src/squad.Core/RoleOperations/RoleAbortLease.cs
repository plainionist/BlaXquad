namespace squad.Core.RoleOperations;

/// <summary>
/// Held by the caller that begins a role abort (the "leader"). Concurrent callers observe an in-flight abort
/// instead of receiving a lease and await its completion. The leader must call exactly one of
/// <see cref="Complete"/> or <see cref="Fail"/> before disposing the lease; disposing always removes the role's
/// in-flight abort entry so a later abort can begin.
/// </summary>
internal sealed class RoleAbortLease : IDisposable
{
    private readonly RoleOperationCoordinator myCoordinator;
    private readonly string myRole;
    private readonly TaskCompletionSource myCompletion;
    private bool myDisposed;

    internal RoleAbortLease(RoleOperationCoordinator coordinator, string role, TaskCompletionSource completion)
    {
        myCoordinator = coordinator;
        myRole = role;
        myCompletion = completion;
    }

    /// <summary>Marks the abort successful, clearing any prior failed-abort barrier for the role.</summary>
    public void Complete()
    {
        myCoordinator.ClearFailedAbort(myRole);
        myCompletion.TrySetResult();
    }

    /// <summary>Marks the abort failed, leaving a barrier closed until a later abort for the role succeeds.</summary>
    public void Fail(Exception exception)
    {
        myCoordinator.MarkFailedAbort(myRole);
        myCompletion.TrySetException(exception);
    }

    public void Dispose()
    {
        if (myDisposed)
            return;
        myDisposed = true;
        myCoordinator.RemoveAbort(myRole);
    }
}
