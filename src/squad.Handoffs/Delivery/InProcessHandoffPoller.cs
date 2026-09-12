using squad.Domain;
namespace squad.Handoffs.Delivery;

/// <summary>
/// Polls role outboxes in-process and exposes unexpected loop termination separately from controlled stopping.
/// Start and stop are idempotent, and a stopped poller may be started again before disposal.
/// </summary>
public sealed class InProcessHandoffPoller : IAsyncDisposable
{
    private static readonly TimeSpan myPollInterval = TimeSpan.FromSeconds(1);
    private readonly IReadOnlyList<SquadMemberDefinition> myMembers;
    private readonly HandoffDeliveryService myDelivery;
    private readonly object mySyncRoot = new();
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? myPollingCancellation;
    private Task? myPolling;
    private bool myDisposed;

    public InProcessHandoffPoller(IReadOnlyList<SquadMemberDefinition> members, IRoleNotifier notifier, HandoffDeliveryLog log)
    {
        myMembers = members;
        myDelivery = new HandoffDeliveryService(notifier, log);
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
                await myDelivery.ProcessOnceAsync(myMembers, cancellationToken: cancellationToken);
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


