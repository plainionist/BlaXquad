namespace squad.AgentProvider.Abstractions;

/// <summary>
/// A generation-bound handle to an active session. A lease proves the session was obtained atomically together
/// with a lifecycle-phase check and the current generation identity, rather than through an unleased lookup that
/// could hand out a session whose generation has since been retired. Disposing a lease releases no resource - it
/// is a plain value, not an owner.
/// </summary>
public readonly struct SessionLease
{
    public SessionLease(int generation, IAgentSession session)
    {
        Generation = generation;
        Session = session;
    }

    public int Generation { get; }
    public IAgentSession Session { get; }
}
