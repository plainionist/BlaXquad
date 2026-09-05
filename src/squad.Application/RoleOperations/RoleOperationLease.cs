namespace squad.Application.RoleOperations;

/// <summary>
/// Holds a role's operation serialization slot until disposed. Callers register the active operation's cancellation
/// source once admission checks pass; disposing the lease unregisters that cancellation source (if any) and
/// releases the role's operation slot for the next caller, in that order.
/// </summary>
internal sealed class RoleOperationLease : IDisposable
{
    private readonly RoleOperationCoordinator myCoordinator;
    private readonly string myRole;
    private readonly SemaphoreSlim myRoleLock;
    private CancellationTokenSource? myOperation;
    private bool myDisposed;

    internal RoleOperationLease(RoleOperationCoordinator coordinator, string role, SemaphoreSlim roleLock)
    {
        myCoordinator = coordinator;
        myRole = role;
        myRoleLock = roleLock;
    }

    /// <summary>
    /// Registers the active operation's cancellation source, cancelling it immediately if the role was invalidated
    /// while this lease was being acquired.
    /// </summary>
    public void Register(CancellationTokenSource operation)
    {
        myOperation = operation;
        myCoordinator.RegisterOperation(myRole, operation);
    }

    public void Dispose()
    {
        if (myDisposed)
            return;
        myDisposed = true;
        if (myOperation is not null)
            myCoordinator.UnregisterOperation(myRole, myOperation);
        myRoleLock.Release();
    }
}
