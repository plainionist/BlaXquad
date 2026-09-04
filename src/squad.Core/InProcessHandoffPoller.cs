using global::squad.Agent.Configuration;
namespace squad.Core;

public sealed class InProcessHandoffPoller : IHandoffPump
{
    private static readonly TimeSpan myPollInterval = TimeSpan.FromSeconds(1);
    private readonly Func<IReadOnlyList<RoleRow>> myRolesProvider;
    private readonly HandoffDeliveryService myDelivery;
    private readonly object mySyncRoot = new();
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? myPollingCancellation;
    private Task? myPolling;
    private bool myDisposed;

    public InProcessHandoffPoller(Func<IReadOnlyList<RoleRow>> rolesProvider, IRoleNotifier notifier, Action<string[]> log)
    {
        myRolesProvider = rolesProvider;
        myDelivery = new HandoffDeliveryService(notifier, log);
    }

    public InProcessHandoffPoller(IReadOnlyList<RoleRow> roles, IRoleNotifier notifier, Action<string[]> log)
        : this(() => roles, notifier, log)
    {
    }

    public Task Failure => myFailure.Task;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (mySyncRoot)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            if (myPolling is not null)
                return Task.CompletedTask;

            myPollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            myPolling = PollAsync(myPollingCancellation.Token);
            return Task.CompletedTask;
        }
    }

    public Task RecoverAsync(CancellationToken cancellationToken = default) =>
        myDelivery.RecoverAsync(myRolesProvider(), cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? polling;
        CancellationTokenSource? pollingCancellation;
        lock (mySyncRoot)
        {
            polling = myPolling;
            pollingCancellation = myPollingCancellation;
        }
        if (polling is null || pollingCancellation is null)
            return;

        pollingCancellation.Cancel();
        await polling.WaitAsync(cancellationToken);
        lock (mySyncRoot)
        {
            if (ReferenceEquals(myPolling, polling))
            {
                myPolling = null;
                myPollingCancellation = null;
                pollingCancellation.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (myDisposed)
            return;
        myDisposed = true;
        await StopAsync();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await myDelivery.ProcessOnceAsync(myRolesProvider(), cancellationToken: cancellationToken);
                await Task.Delay(myPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            myFailure.TrySetException(exception);
        }
    }
}



