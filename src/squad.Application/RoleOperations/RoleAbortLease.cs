namespace squad.Application.RoleOperations;

/// <summary>
/// Identifies the caller responsible for completing a role abort while concurrent callers await the same operation.
/// The leader must call exactly one of <see cref="Complete"/> or <see cref="Fail"/> before disposal, which removes
/// the in-flight entry so a later abort can begin.
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
        {
            return;
        }
        myDisposed = true;
        myCoordinator.RemoveAbort(myRole);
    }
}
