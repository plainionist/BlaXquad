namespace squad.Host.Runtime;

/// <summary>
/// Resolves one phase transition owned by <see cref="SessionRegistry"/>. The caller must commit or fail exactly
/// once; disposing an unresolved transition fails it so transition exclusion is always released.
/// </summary>
internal sealed class SquadLifecycleTransition : IDisposable
{
    private readonly Action myCommit;
    private readonly Action myFail;
    private bool myResolved;

    internal SquadLifecycleTransition(Action commit, Action fail)
    {
        myCommit = commit;
        myFail = fail;
    }

    public void Commit()
    {
        Resolve(myCommit);
    }

    public void Fail()
    {
        Resolve(myFail);
    }

    public void Dispose()
    {
        if (!myResolved)
        {
            Fail();
        }
    }

    private void Resolve(Action resolution)
    {
        if (myResolved)
        {
            throw new InvalidOperationException("The lifecycle transition was already resolved.");
        }
        myResolved = true;
        resolution();
    }
}
