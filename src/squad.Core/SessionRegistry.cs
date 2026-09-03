using global::squad.Abstractions;
using global::squad.Abstractions.Agents;
namespace squad.Core;

public sealed class SessionRegistry
{
    private readonly Dictionary<string, IAgentSession> mySessions = new(StringComparer.Ordinal);
    private bool myStopping;

    public void Register(IAgentSession session)
    {
        if (myStopping)
            throw new InvalidOperationException("Squad is shutting down");
        mySessions.Add(session.Role, session);
    }

    public IAgentSession GetActive(string role)
    {
        if (myStopping)
            throw new InvalidOperationException("Squad is shutting down");
        if (!mySessions.TryGetValue(role, out var session))
            throw new InvalidOperationException($"No registered session for role '{role}'");
        if (session.Completion.IsCompleted)
            throw new InvalidOperationException($"Session for role '{role}' is no longer active");
        return session;
    }

    public void BeginStopping() => myStopping = true;
}



