namespace squad.Handoffs.Delivery;

/// <summary>Owns handoff recovery and continuous delivery for the active session generation.</summary>
public interface IHandoffPump : IAsyncDisposable
{
    /// <summary>Faults when background delivery terminates unexpectedly; normal stopping leaves it incomplete.</summary>
    Task Failure { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    /// <summary>Notifies roles that already have queued inbox work before polling begins.</summary>
    Task RecoverAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}


