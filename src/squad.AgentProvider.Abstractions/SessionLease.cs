namespace squad.AgentProvider.Abstractions;

/// <summary>
/// Proves that a session was obtained atomically with an accepting lifecycle phase and the current generation
/// identity. This value does not own or extend the lifetime of the session.
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
