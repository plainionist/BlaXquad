using squad.AgentProvider.Abstractions;

namespace squad.Specs.Support;

/// <summary>
/// Provider-side runtime for <see cref="FakeAgentProviderFactory"/>. Establishes one <see cref="FakeAgentSession"/>
/// per configured role through the production startup callback, and disposes every session it created - the
/// normal production <see cref="IAgentRuntime"/> lifecycle this slice needs to prove.
/// </summary>
internal sealed class FakeAgentRuntime(AgentBackendContext context) : IAgentRuntime
{
    private readonly List<FakeAgentSession> mySessions = [];

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        foreach (var role in context.Roles)
        {
            var session = new FakeAgentSession(role.Role);
            mySessions.Add(session);
            await sessionStarted(session);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in mySessions)
        {
            await session.DisposeAsync();
        }
    }
}
