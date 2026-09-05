using global::squad.Handoffs.Delivery;

namespace squad.Specs.Support;

public sealed class RecordingHandoffPump : IHandoffPump
{
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Disposed { get; private set; }
    public bool FailOnDispose { get; set; }
    public Task Failure => myFailure.Task;
    public LifecycleTrace? Trace { get; set; }
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Trace?.Record("handoff.started");
        return Task.CompletedTask;
    }
    public Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        Trace?.Record("handoff.recovered");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Trace?.Record("handoff.stopped");
        return Task.CompletedTask;
    }
    public void Fail() => myFailure.TrySetException(new InvalidOperationException("recording handoff pump failed"));
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        Trace?.Record("handoff.disposed");
        if (FailOnDispose)
            throw new InvalidOperationException("recording handoff pump disposal failed");
        return ValueTask.CompletedTask;
    }
}



