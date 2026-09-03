namespace squad.Abstractions;

public interface IAgentBackend : IAsyncDisposable
{
    Task PrepareAsync(CancellationToken cancellationToken = default);
    Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default);
}



