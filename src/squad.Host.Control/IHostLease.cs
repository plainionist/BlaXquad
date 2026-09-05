namespace squad.Host.Control;

public interface IHostLease : IAsyncDisposable
{
    Task ShutdownRequested { get; }
    Task ServerFailure { get; }
    void SetAgentReadinessProvider(Func<string, CancellationToken, Task<bool?>> provider);
}



