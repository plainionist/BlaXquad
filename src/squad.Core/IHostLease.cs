namespace squad.Core;

public interface IHostLease : IAsyncDisposable
{
    Task ShutdownRequested { get; }
    Task ServerFailure { get; }
    void SetAgentReadinessProvider(Func<string, CancellationToken, Task<bool?>> provider);
}



