using squad.AgentProvider.Abstractions;

namespace squadHQ.Commands;

/// <summary>
/// The single lifecycle authority for one squad process. It owns the current phase (Created, Starting, Running,
/// Stopping, Stopped), the current generation identity, transition exclusion, and command admission - the one
/// decision of whether any new work may still be admitted. The session-by-role catalog is delegated to
/// <see cref="SessionCatalog"/> so this type stays a thin aggregate rather than one undifferentiated class.
/// </summary>
internal sealed class SessionRegistry : ISessionAdmission
{
    private readonly object myLock = new();
    private readonly SessionCatalog myCatalog = new();
    private SquadLifecyclePhase myPhase = SquadLifecyclePhase.Created;
    private bool myTransitionInProgress;
    private int myGeneration;

    public SquadLifecyclePhase Phase
    {
        get { lock (myLock) return myPhase; }
    }

    public bool IsAccepting
    {
        get { lock (myLock) return IsAcceptingCore(); }
    }

    /// <summary>
    /// Begins the one transition from <see cref="SquadLifecyclePhase.Created"/> to
    /// <see cref="SquadLifecyclePhase.Starting"/> and bumps the generation identity. Committing moves to
    /// <see cref="SquadLifecyclePhase.Running"/>; failing releases transition exclusion without changing phase, so
    /// a subsequent <see cref="BeginStopping"/> can still unwind a partially started generation.
    /// </summary>
    public SquadLifecycleTransition BeginStarting()
    {
        lock (myLock)
        {
            RequireNoTransitionInProgress();
            if (myPhase != SquadLifecyclePhase.Created)
                throw new InvalidOperationException($"Cannot begin starting from phase '{myPhase}'.");
            myTransitionInProgress = true;
            myGeneration++;
            myPhase = SquadLifecyclePhase.Starting;
        }
        return new SquadLifecycleTransition(
            commit: () => EndTransition(SquadLifecyclePhase.Running),
            fail: () => EndTransition(SquadLifecyclePhase.Starting));
    }

    /// <summary>
    /// Begins the one transition to <see cref="SquadLifecyclePhase.Stopping"/> from any phase except Stopping or
    /// Stopped, closing command admission immediately. Both commit and fail land on
    /// <see cref="SquadLifecyclePhase.Stopped"/>: cleanup collects and reports failures rather than leaving the
    /// registry in an ambiguous phase.
    /// </summary>
    public SquadLifecycleTransition BeginStopping()
    {
        lock (myLock)
        {
            RequireNoTransitionInProgress();
            if (myPhase is SquadLifecyclePhase.Stopping or SquadLifecyclePhase.Stopped)
                throw new InvalidOperationException($"Cannot begin stopping from phase '{myPhase}'.");
            myTransitionInProgress = true;
            myPhase = SquadLifecyclePhase.Stopping;
        }
        return new SquadLifecycleTransition(
            commit: () => EndTransition(SquadLifecyclePhase.Stopped),
            fail: () => EndTransition(SquadLifecyclePhase.Stopped));
    }

    public void Register(IAgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (myLock)
        {
            if (!IsAcceptingCore())
                throw new InvalidOperationException("Squad is shutting down");
            myCatalog.Register(session);
        }
    }

    public bool TryLeaseSession(string role, out SessionLease lease)
    {
        lock (myLock)
        {
            if (IsAcceptingCore() && myCatalog.TryGetActive(role, out var session))
            {
                lease = new SessionLease(myGeneration, session);
                return true;
            }
        }
        lease = default;
        return false;
    }

    private bool IsAcceptingCore() =>
        myPhase is SquadLifecyclePhase.Created or SquadLifecyclePhase.Starting or SquadLifecyclePhase.Running;

    private void RequireNoTransitionInProgress()
    {
        if (myTransitionInProgress)
            throw new InvalidOperationException("A lifecycle transition is already in progress.");
    }

    private void EndTransition(SquadLifecyclePhase phase)
    {
        lock (myLock)
        {
            myPhase = phase;
            myTransitionInProgress = false;
        }
    }
}
