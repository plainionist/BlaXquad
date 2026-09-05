namespace squad.AgentProvider.Abstractions;

/// <summary>
/// One backend generation's runtime handle, created before the fallible per-role session startup begins. The
/// runtime exclusively owns its provider client connection and every <see cref="IAgentSession"/> it creates;
/// nothing outside the runtime disposes those sessions or the client directly, including after a partial startup
/// failure.
/// </summary>
public interface IAgentRuntime : IAsyncDisposable
{
    Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default);
}
