using global::squad.AgentProvider.Abstractions;

namespace squadHQ.Commands;

/// <summary>
/// The session-by-role catalog for one <see cref="SessionRegistry"/> generation. Not itself thread-safe - every
/// method must be called while holding the owning registry's lock, which is also what makes a phase check and a
/// catalog lookup atomic with each other.
/// </summary>
internal sealed class SessionCatalog
{
    private readonly Dictionary<string, IAgentSession> mySessions = new(StringComparer.Ordinal);

    public void Register(IAgentSession session) => mySessions.Add(session.Role, session);

    public bool TryGetActive(string role, out IAgentSession session)
    {
        if (mySessions.TryGetValue(role, out var candidate) && !candidate.Completion.IsCompleted)
        {
            session = candidate;
            return true;
        }
        session = null!;
        return false;
    }
}
