namespace squad.Handoffs;

public interface IHandoffPump : IAsyncDisposable
{
    Task Failure { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RecoverAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}



