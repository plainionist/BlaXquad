using global::squad.Hosting.Abstractions;

namespace squad.Specs.Support;

public sealed class RecordingSleepInhibitor : ISleepInhibitor
{
    private readonly TaskCompletionSource myStartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myStartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> CommandPrefix => [];
    public bool BlockStart { get; set; }
    public bool FailWhenCanceled { get; set; }
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }
    public Task StartEntered => myStartEntered.Task;
    public LifecycleTrace? Trace { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Started = true;
        Trace?.Record("sleepInhibitor.started");
        myStartEntered.TrySetResult();
        if (BlockStart)
        {
            try
            {
                await myStartGate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (FailWhenCanceled)
            {
                throw new InvalidOperationException("recording startup cancellation failed");
            }
        }
    }

    public void ReleaseStart() => myStartGate.TrySetResult();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        Trace?.Record("sleepInhibitor.disposed");
        return ValueTask.CompletedTask;
    }
}



