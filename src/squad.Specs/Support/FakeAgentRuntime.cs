using squad.AgentProvider.Abstractions;

namespace squad.Specs.Support;

/// <summary>
/// Provider-side runtime for <see cref="FakeAgentProviderFactory"/>. Establishes one <see cref="FakeAgentSession"/>
/// per configured role through the production startup callback, and disposes every session it created - the
/// normal production <see cref="IAgentRuntime"/> lifecycle this slice needs to prove. When the environment names a
/// fake-provider control pipe (<see cref="FakeProviderControlServer.PipeNameEnvironmentVariable"/>), also connects
/// to it and reports every session start and disposal across it; otherwise behaves exactly as it did before the
/// control transport existed.
/// </summary>
internal sealed class FakeAgentRuntime(AgentBackendContext context) : IAgentRuntime
{
    private readonly List<FakeAgentSession> mySessions = [];
    private FakeProviderControlClient? myControl;

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        myControl = await FakeProviderControlClient.ConnectIfConfiguredAsync(cancellationToken);

        foreach (var role in context.Roles)
        {
            var session = new FakeAgentSession(role.Role);
            mySessions.Add(session);
            await sessionStarted(session);
            if (myControl is not null)
            {
                await myControl.NotifySessionStartedAsync(session.Role, session.SessionId, cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in mySessions)
        {
            await session.DisposeAsync();
            if (myControl is not null)
            {
                await myControl.NotifySessionDisposedAsync(session.Role, session.SessionId, CancellationToken.None);
            }
        }
        if (myControl is not null)
        {
            await myControl.DisposeAsync();
        }
    }
}
