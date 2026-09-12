namespace squad.Application.Members;

/// <summary>
/// Holds a member's operation serialization slot until disposed. Callers register the active operation's
/// cancellation source once admission checks pass; disposing the lease unregisters that cancellation source (if
/// any) and releases the member's operation slot for the next caller, in that order.
/// </summary>
internal sealed class OperationLease : IDisposable
{
    private readonly SquadMemberAggregate myMember;
    private readonly SemaphoreSlim myOperationLock;
    private CancellationTokenSource? myOperation;
    private bool myDisposed;

    internal OperationLease(SquadMemberAggregate member, SemaphoreSlim operationLock)
    {
        myMember = member;
        myOperationLock = operationLock;
    }

    /// <summary>
    /// Registers the active operation's cancellation source, cancelling it immediately if the member was
    /// invalidated while this lease was being acquired.
    /// </summary>
    public void Register(CancellationTokenSource operation)
    {
        Contract.Invariant(myOperation is null, "An OperationLease can register at most one operation.");
        myOperation = operation;
        myMember.RegisterOperation(operation);
    }

    public void Dispose()
    {
        if (myDisposed)
        {
            return;
        }
        myDisposed = true;
        if (myOperation is not null)
        {
            myMember.UnregisterOperation(myOperation);
        }
        myOperationLock.Release();
    }
}
