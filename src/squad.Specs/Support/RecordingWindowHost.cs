using squad.Hosting.Abstractions;

namespace squad.Specs.Support;

public sealed class RecordingWindowHost : IWindowHost
{
    private readonly TaskCompletionSource myClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool HasCloseSignal => myClosed.Task.IsCompletedSuccessfully;
    public bool FailOnStart { get; set; }
    public bool FailOnSessionsStarted { get; set; }
    public bool FailOnClose { get; set; }
    public bool CancelOnClose { get; set; }
    public Action? OnStart { get; set; }
    public Action? OnSessionsStarted { get; set; }
    public Action? OnStop { get; set; }
    public LifecycleTrace? Trace { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        OnStart?.Invoke();
        Trace?.Record("window.started");
        if (FailOnStart)
            throw new InvalidOperationException("recording window start failed");
        return Task.CompletedTask;
    }

    public Task SessionsStartedAsync(CancellationToken cancellationToken = default)
    {
        Trace?.Record("window.sessionsStarted");
        if (FailOnSessionsStarted)
            throw new InvalidOperationException("recording window sessions-started failed");
        OnSessionsStarted?.Invoke();
        return Task.CompletedTask;
    }
    public Task WaitForCloseAsync(CancellationToken cancellationToken = default)
    {
        if (FailOnClose)
            return Task.FromException(new InvalidOperationException("recording window close failed"));
        if (CancelOnClose)
            return Task.FromCanceled(new CancellationToken(true));
        return myClosed.Task.WaitAsync(cancellationToken);
    }

    public void Close() => myClosed.TrySetResult();

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        Trace?.Record("window.stopped");
        OnStop?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        Trace?.Record("window.disposed");
        return ValueTask.CompletedTask;
    }
}


