namespace squad.AgentProvider.Abstractions;

/// <summary>
/// Owns one backend generation's provider connection and every <see cref="IAgentSession"/> it creates, including
/// sessions created during a partially failed startup. Callers dispose only the runtime, never its sessions or
/// provider connection directly.
/// </summary>
public interface IAgentRuntime : IAsyncDisposable
{
    /// <summary>
    /// Starts all configured sessions and reports each usable session through <paramref name="sessionStarted"/>.
    /// Startup failure must retire every resource already created by this runtime.
    /// </summary>
    Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default);
}
