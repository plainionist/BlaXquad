namespace squad.Hosting.Abstractions;

/// <summary>
/// Defines the native-window lifecycle independently of the UI protocol. Startup does not complete until the UI is
/// ready to receive session state.
/// </summary>
public interface IWindowHost : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    /// <summary>Notifies the UI that role sessions are registered and initial state can be published.</summary>
    Task SessionsStartedAsync(CancellationToken cancellationToken = default);
    Task WaitForCloseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
