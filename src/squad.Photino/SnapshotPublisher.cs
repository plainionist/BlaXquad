using global::squad.Abstractions;
using global::squad.Abstractions.Agents;
using System.Diagnostics;
using System.Threading.Channels;

namespace squad.Photino;

internal sealed class SnapshotPublisher : IAsyncDisposable
{
    private const int myNoRequest = 0;
    private const int myDeferredRequest = 1;
    private const int myImmediateRequest = 2;
    private readonly Func<Task> myPublish;
    private readonly TimeSpan myInterval;
    private readonly Channel<bool> myRequests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource myShutdown = new();
    private readonly object myDisposeLock = new();
    private readonly Task myWorker;
    private Task? myDispose;
    private int myPendingPriority;
    private int myDisposed;

    internal SnapshotPublisher(Func<Task> publish, TimeSpan interval)
    {
        myPublish = publish;
        myInterval = interval;
        myWorker = RunAsync();
    }

    internal void Request(UiRefreshPriority priority)
    {
        if (Volatile.Read(ref myDisposed) != 0)
            return;

        var requestedPriority = priority == UiRefreshPriority.Immediate
            ? myImmediateRequest
            : myDeferredRequest;
        UpgradePendingPriority(requestedPriority);
        myRequests.Writer.TryWrite(true);
    }

    public ValueTask DisposeAsync()
    {
        lock (myDisposeLock)
            return new ValueTask(myDispose ??= DisposeCoreAsync());
    }

    private async Task RunAsync()
    {
        try
        {
            while (await myRequests.Reader.WaitToReadAsync(myShutdown.Token))
            {
                DrainSignals();
                var priority = Interlocked.Exchange(ref myPendingPriority, myNoRequest);
                if (priority == myNoRequest)
                    continue;
                if (priority == myDeferredRequest)
                    await WaitForDeferredPublicationAsync(myShutdown.Token);
                await myPublish();
            }
        }
        catch (OperationCanceledException) when (myShutdown.IsCancellationRequested)
        {
        }
    }

    private async Task WaitForDeferredPublicationAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var remaining = myInterval - elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(remaining, waitCancellation.Token);
            var request = myRequests.Reader.WaitToReadAsync(waitCancellation.Token).AsTask();
            var completed = await Task.WhenAny(delay, request);
            await waitCancellation.CancelAsync();
            if (completed == delay)
            {
                await delay;
                break;
            }
            await request;

            DrainSignals();
            var priority = Interlocked.Exchange(ref myPendingPriority, myNoRequest);
            if (priority == myImmediateRequest)
                break;
        }

        DrainSignals();
        Interlocked.Exchange(ref myPendingPriority, myNoRequest);
    }

    private void UpgradePendingPriority(int requestedPriority)
    {
        while (true)
        {
            var currentPriority = Volatile.Read(ref myPendingPriority);
            if (currentPriority >= requestedPriority)
                return;
            if (Interlocked.CompareExchange(
                    ref myPendingPriority,
                    requestedPriority,
                    currentPriority) == currentPriority)
                return;
        }
    }

    private void DrainSignals()
    {
        while (myRequests.Reader.TryRead(out _))
        {
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref myDisposed, 1) != 0)
            return;
        myRequests.Writer.TryComplete();
        await myShutdown.CancelAsync();
        await myWorker;
        myShutdown.Dispose();
    }
}



