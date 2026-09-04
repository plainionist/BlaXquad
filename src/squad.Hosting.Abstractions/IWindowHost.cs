namespace squad.Hosting.Abstractions;

public interface IWindowHost : IAsyncDisposable
{
    bool HasCloseSignal { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SessionsStartedAsync(CancellationToken cancellationToken = default);
    Task WaitForCloseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}



