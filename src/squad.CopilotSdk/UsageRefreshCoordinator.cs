namespace squad.CopilotSdk;

/// <summary>
/// Owns the fixed five-second, event-aware usage refresh policy for one Copilot SDK session. SDK activity marks
/// the session dirty and ensures one refresh cycle is scheduled per window; a session-idle transition stops active
/// scheduling and runs one final refresh per metric, retaining that final request when a metric refresh is already
/// in flight rather than discarding it. The interval is a fixed implementation policy: no clock, scheduler,
/// callback, or configuration seam is exposed.
/// </summary>
internal sealed class UsageRefreshCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan myRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly MetricRefresher myContext;
    private readonly MetricRefresher myUsage;
    private readonly CancellationTokenSource myLifetime = new();
    private readonly object myLock = new();
    private bool myActive;
    private bool myDirty;
    private CancellationTokenSource? myWindowCancellation;
    private Task? myWindowLoop;
    private bool myDisposed;

    public UsageRefreshCoordinator(Func<CancellationToken, Task> refreshContext, Func<CancellationToken, Task> refreshUsage)
    {
        myContext = new MetricRefresher(refreshContext);
        myUsage = new MetricRefresher(refreshUsage);
    }

    /// <summary>Runs the immediate initial refresh performed once when a session is attached.</summary>
    public void RunInitialRefresh()
    {
        if (myDisposed)
        {
            return;
        }
        myContext.Run(myLifetime.Token);
        myUsage.Run(myLifetime.Token);
    }

    /// <summary>Marks the session dirty from SDK activity and ensures a refresh window is scheduled.</summary>
    public void NotifyActivity()
    {
        lock (myLock)
        {
            if (myDisposed)
            {
                return;
            }
            myDirty = true;
            if (!myActive)
            {
                myActive = true;
                myWindowCancellation = CancellationTokenSource.CreateLinkedTokenSource(myLifetime.Token);
                myWindowLoop = RunWindowLoopAsync(myWindowCancellation.Token);
            }
        }
    }

    /// <summary>
    /// Stops active refresh scheduling and requests one final refresh of both metrics. A metric refresh already in
    /// flight retains this request and runs it once the in-flight call completes.
    /// </summary>
    public void NotifyIdle()
    {
        CancellationTokenSource? windowCancellation;
        lock (myLock)
        {
            if (myDisposed)
            {
                return;
            }
            myActive = false;
            myDirty = false;
            windowCancellation = myWindowCancellation;
            myWindowCancellation = null;
        }
        windowCancellation?.Cancel();
        myContext.RunFinal(myLifetime.Token);
        myUsage.RunFinal(myLifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        Task? windowLoop;
        lock (myLock)
        {
            if (myDisposed)
            {
                return;
            }
            myDisposed = true;
            myActive = false;
            myDirty = false;
            windowLoop = myWindowLoop;
        }

        myLifetime.Cancel();

        if (windowLoop is not null)
        {
            try
            {
                await windowLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await myContext.WaitForCompletionAsync().ConfigureAwait(false);
        await myUsage.WaitForCompletionAsync().ConfigureAwait(false);
        myLifetime.Dispose();
    }

    private async Task RunWindowLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(myRefreshInterval, cancellationToken).ConfigureAwait(false);

                bool shouldRefresh;
                lock (myLock)
                {
                    if (!myActive)
                    {
                        return;
                    }
                    shouldRefresh = myDirty;
                    myDirty = false;
                }

                if (shouldRefresh)
                {
                    myContext.Run(cancellationToken);
                    myUsage.Run(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Runs one metric's refresh with in-flight exclusion: a request that arrives while a refresh is already
    /// running is either skipped (normal window trigger) or retained to run once more after completion (final
    /// idle trigger). Transient failures are swallowed here so later activity or idle remains free to retry.
    /// </summary>
    private sealed class MetricRefresher
    {
        private readonly Func<CancellationToken, Task> myWork;
        private readonly object myLock = new();
        private Task? myInFlight;
        private bool myFinalPending;

        public MetricRefresher(Func<CancellationToken, Task> work) => myWork = work;

        public void Run(CancellationToken cancellationToken)
        {
            lock (myLock)
            {
                if (myInFlight is not null)
                {
                    return;
                }
                myInFlight = ExecuteLoopAsync(cancellationToken);
            }
        }

        public void RunFinal(CancellationToken cancellationToken)
        {
            lock (myLock)
            {
                if (myInFlight is not null)
                {
                    myFinalPending = true;
                    return;
                }
                myInFlight = ExecuteLoopAsync(cancellationToken);
            }
        }

        public async Task WaitForCompletionAsync()
        {
            Task? current;
            lock (myLock)
            {
                current = myInFlight;
            }
            if (current is null)
            {
                return;
            }
            try
            {
                await current.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task ExecuteLoopAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    await myWork(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Transient failures are non-fatal: leave the metric eligible for the next activity or idle
                    // trigger rather than treating this refresh as a session failure.
                }

                lock (myLock)
                {
                    if (myFinalPending)
                    {
                        myFinalPending = false;
                        continue;
                    }
                    myInFlight = null;
                    return;
                }
            }
        }
    }
}
