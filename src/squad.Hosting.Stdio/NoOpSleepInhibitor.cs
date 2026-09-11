using squad.Hosting.Abstractions;

namespace squad.Hosting.Stdio;

/// <summary>No-op <see cref="ISleepInhibitor"/> for the headless stdio host: a test process never needs to keep
/// the machine awake, and this plug-in must never construct the Photino-hosted <c>SleepInhibitor</c> or take any
/// dependency on that assembly.</summary>
sealed class NoOpSleepInhibitor : ISleepInhibitor
{
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
