namespace squad.CopilotSdk;

/// <summary>
/// Owns the fixed five-second, event-aware AIC usage refresh policy for one Copilot SDK session. SDK activity marks
/// the session dirty and ensures one refresh cycle is scheduled per window; a session-idle transition stops active
/// scheduling and runs one final refresh, retaining that final request when a refresh is already
/// in flight rather than discarding it. Metric work always runs on the coordinator's own lifetime token, never the
/// per-window scheduling token, so an idle race can never cancel a refresh that is already under way or retained
/// for final delivery; only <see cref="DisposeAsync"/> (session teardown or failure) stops that work. The interval
/// is a fixed implementation policy: no clock, scheduler, callback, or configuration seam is exposed.
/// </summary>
internal sealed class UsageRefreshCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan myRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly MetricRefresher myUsage;
    private readonly CancellationTokenSource myLifetime = new();
    private readonly object myLock = new();
    private bool myActive;
    private bool myDirty;
    private CancellationTokenSource? myWindowCancellation;
    private Task? myWindowLoop;
    private bool myDisposed;

    public UsageRefreshCoordinator(Func<CancellationToken, Task> refreshUsage)
    {
        myUsage = new MetricRefresher(refreshUsage);
    }

    /// <summary>Runs the immediate initial refresh performed once when a session is attached.</summary>
    public void RunInitialRefresh()
    {
        if (myDisposed)
        {
            return;
        }
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
    /// Stops active refresh scheduling and requests one final refresh. A refresh already in
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
                    // Metric work runs on the coordinator lifetime token, not the window token: cancelling this
                    // window (e.g. an idle race) must never cancel a refresh already under way.
                    myUsage.Run(myLifetime.Token);
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
