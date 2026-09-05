using global::squadHQ.Commands;

namespace squad.Specs.Support;

public sealed class FaultingHostLease : IHostLease
{
    private readonly HostLease myLease;
    private readonly TaskCompletionSource myServerFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FaultingHostLease(HostLease lease) => myLease = lease;

    public Task ShutdownRequested => myLease.ShutdownRequested;
    public Task ServerFailure => myServerFailure.Task;

    public void FailServer(Exception exception) => myServerFailure.TrySetException(exception);
    public void SetAgentReadinessProvider(Func<string, CancellationToken, Task<bool?>> provider) => myLease.SetAgentReadinessProvider(provider);

    public ValueTask DisposeAsync() => myLease.DisposeAsync();
}



