using global::squadHQ.Commands;

namespace squad.Specs.Support;

public sealed class RecordingHostLease : IHostLease
{
    private readonly TaskCompletionSource myShutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myServerFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ShutdownRequested => myShutdown.Task;
    public Task ServerFailure => myServerFailure.Task;
    public bool Disposed { get; private set; }
    public Func<string, CancellationToken, Task<bool?>>? AgentReadinessProvider { get; private set; }
    public LifecycleTrace? Trace { get; set; }

    public void RequestShutdown() => myShutdown.TrySetResult();
    public void FailServer(Exception exception) => myServerFailure.TrySetException(exception);
    public void SetAgentReadinessProvider(Func<string, CancellationToken, Task<bool?>> provider)
    {
        AgentReadinessProvider = provider;
        Trace?.Record("readinessProvider.installed");
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}


