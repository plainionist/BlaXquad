namespace squad.Hosting.Abstractions;

public interface ISleepInhibitor : IAsyncDisposable
{
    IReadOnlyList<string> CommandPrefix { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
}



