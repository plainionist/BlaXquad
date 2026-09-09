using squad.Configuration;
namespace squad.Handoffs.Delivery;

/// <summary>
/// Polls role outboxes in-process and exposes unexpected loop termination separately from controlled stopping.
/// Start and stop are idempotent, and a stopped poller may be started again before disposal.
/// </summary>
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
            {
                return Task.CompletedTask;
            }
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
        {
            return;
        }
        pollingCancellation.Cancel();
        // Only awaits normal, cooperative-cancellation completion here: a poll loop that already faulted has
        // already reported that same exception through Failure, so re-observing it here would surface it a
        // second time as a spurious, duplicate cleanup failure alongside the real, already-primary one.
        try
        {
            await polling.WaitAsync(cancellationToken);
        }
        catch (Exception) when (myFailure.Task.IsFaulted)
        {
        }
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
        {
            return;
        }
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


