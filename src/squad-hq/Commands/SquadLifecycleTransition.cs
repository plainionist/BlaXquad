namespace squadHQ.Commands;

/// <summary>
/// An explicit handle for one phase transition owned by <see cref="SessionRegistry"/>. A successful <c>Begin*</c>
/// call returns exactly one transition, and the caller must resolve it with exactly one <see cref="Commit"/> or
/// <see cref="Fail"/>. Disposing an unresolved transition treats it as a failure, so transition exclusion is
/// always released even if an unexpected exception bypassed explicit resolution. This handle owns no I/O resource
/// - it exists purely to serialize phase changes and guarantee every transition is eventually released.
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
            Fail();
    }

    private void Resolve(Action resolution)
    {
        if (myResolved)
            throw new InvalidOperationException("The lifecycle transition was already resolved.");
        myResolved = true;
        resolution();
    }
}
