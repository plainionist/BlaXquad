namespace squad.Application.Members;

/// <summary>
/// Identifies the caller responsible for completing a member's abort while concurrent callers await the same
/// operation. The leader must call exactly one of <see cref="Complete"/> or <see cref="Fail"/> before disposal,
/// which removes the in-flight entry so a later abort can begin.
/// </summary>
internal sealed class AbortLease : IDisposable
{
    private readonly MemberAggregate myMember;
    private readonly TaskCompletionSource myCompletion;
    private bool myDisposed;

    internal AbortLease(MemberAggregate member, TaskCompletionSource completion)
    {
        myMember = member;
        myCompletion = completion;
    }

    /// <summary>Marks the abort successful, clearing any prior failed-abort barrier for the member.</summary>
    public void Complete()
    {
        myMember.ClearFailedAbort();
        myCompletion.TrySetResult();
    }

    /// <summary>Marks the abort failed, leaving a barrier closed until a later abort for the member succeeds.</summary>
    public void Fail(Exception exception)
    {
        myMember.MarkFailedAbort();
        myCompletion.TrySetException(exception);
    }

    public void Dispose()
    {
        if (myDisposed)
        {
            return;
        }
        myDisposed = true;
        myMember.RemoveAbort();
    }
}
