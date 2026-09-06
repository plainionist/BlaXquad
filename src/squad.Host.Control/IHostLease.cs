namespace squad.Host.Control;

/// <summary>
/// Owns exclusive host identity and its control endpoint for one project root. The exposed tasks distinguish
/// requested shutdown from unexpected control-server failure.
/// </summary>
public interface IHostLease : IAsyncDisposable
{
    Task ShutdownRequested { get; }
    Task ServerFailure { get; }
    void SetAgentReadinessProvider(Func<string, CancellationToken, Task<bool?>> provider);
}


