namespace squad.Hosting.Abstractions;

/// <summary>Owns the platform mechanism that prevents system sleep for the host lifetime.</summary>
public interface ISleepInhibitor : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
}

