namespace squad.Hosting.Abstractions;

/// <summary>Owns the platform mechanism that prevents system sleep for the host lifetime.</summary>
public interface ISleepInhibitor : IAsyncDisposable
{
    /// <summary>Gets the detected external command prefix, or an empty list when inhibition uses a native API or is unavailable.</summary>
    IReadOnlyList<string> CommandPrefix { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
}

