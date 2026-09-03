using global::squad.Core;

namespace squad.Specs.Support;

public sealed class RecordingHandoffPump : IHandoffPump
{
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Disposed { get; private set; }
    public bool FailOnDispose { get; set; }
    public Task Failure => myFailure.Task;
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecoverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Fail() => myFailure.TrySetException(new InvalidOperationException("recording handoff pump failed"));
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        if (FailOnDispose)
            throw new InvalidOperationException("recording handoff pump disposal failed");
        return ValueTask.CompletedTask;
    }
}



