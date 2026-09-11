using squad.Hosting.Abstractions;
using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application;
using squad.Handoffs.Delivery;
using squad.Host.Control;
using squad.Workspaces;
using System.Runtime.ExceptionServices;

namespace squad.Host.Runtime;

/// <summary>
/// Owns the process-wide startup, running, and cleanup lifecycle, including the window, backend generation,
/// handoff pump, sleep inhibitor, and host lease.
/// </summary>
public sealed class SquadApplication : IAsyncDisposable
{
    private static readonly Task myNever = Task.Delay(Timeout.InfiniteTimeSpan);
    private readonly LaunchPreparer myLaunchPreparer;
    private readonly IAgentProviderFactory myAgentProviderFactory;
    private readonly SessionRoleNotifier myHandoffNotifier;
    private readonly IWindowHost myWindowHost;
    private readonly ISleepInhibitor mySleepInhibitor;
    private readonly SquadViewModel myViewModel;
    private readonly HostLease myHostLease;
    private readonly SessionRegistry mySessionRegistry;
    private readonly CancellationTokenSource myStopping = new();
    private readonly object myCleanupLock = new();
    private IAgentBackend? myAgentBackend;
    private InProcessHandoffPoller? myHandoffPump;
    private SquadRuntimeController? myRuntimeController;
    private Task<IReadOnlyList<Exception>>? myCleanup;
    private bool myWindowStarted;

    /// <summary>
    /// Creates an application whose handoff notifier and command dispatch share one session registry, keeping
    /// notification routing atomic with lifecycle admission. This is the single production composition path.
    /// </summary>
    public static SquadApplication Create(
        LaunchPreparer launchPreparer,
        IAgentProviderFactory agentProviderFactory,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        SquadViewModel viewModel,
        HostLease hostLease)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(hostLease);

        var sessionRegistry = new SessionRegistry();
        var notifier = new SessionRoleNotifier(sessionRegistry, viewModel);
        return new SquadApplication(
            launchPreparer,
            agentProviderFactory,
            notifier,
            windowHost,
            sleepInhibitor,
            sessionRegistry,
            viewModel,
            hostLease);
    }

    // Private composition seam used exclusively by Create above: lets the production creation path share one
    // SessionRegistry instance between SquadApplication and SessionRoleNotifier without exposing the
    // registry-sharing constructor as public API.
    private SquadApplication(
        LaunchPreparer launchPreparer,
        IAgentProviderFactory agentProviderFactory,
        SessionRoleNotifier handoffNotifier,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        SessionRegistry sessionRegistry,
        SquadViewModel viewModel,
        HostLease hostLease)
    {
        myLaunchPreparer = launchPreparer;
        myAgentProviderFactory = agentProviderFactory;
        myHandoffNotifier = handoffNotifier;
        myWindowHost = windowHost;
        mySleepInhibitor = sleepInhibitor;
        myViewModel = viewModel;
        myHostLease = hostLease;
        mySessionRegistry = sessionRegistry;
        myViewModel.UseAdmission(sessionRegistry);
    }

    /// <summary>
    /// Runs startup through readiness, then waits for shutdown, window closure, cancellation, or an owned-resource
    /// failure. Cleanup always runs; cleanup failures are preserved alongside the primary failure.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ExceptionDispatchInfo? primary = null;
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var shutdown = myHostLease.ShutdownRequested;
        var serverFailure = myHostLease.ServerFailure;
        // The handoff pump does not exist until startup reaches its construction after preparation, so no fatal
        // handoff signal can fire before then; it is recomputed from the now-owned pump once the startup task has
        // fully completed, in the same race-safe manner as the late-created backend failure below.
        var handoffFailure = myNever;
        // The backend does not exist until startup reaches provider creation, so no fatal-backend signal can fire
        // before then; it is recomputed from the now-owned backend once the startup task has fully completed.
        var backendFailure = myNever;
        var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        Task? startup = null;
        var startupObserved = false;
        var terminationFailures = new List<Exception>();

        try
        {
            ThrowForTerminalSignal(serverFailure, handoffFailure, backendFailure, shutdown, cancellationToken);
            startup = StartCoreAsync(startupCancellation.Token);
            await Task.WhenAny(startup, shutdown, serverFailure, handoffFailure, backendFailure, cancellation);
            ThrowForTerminalSignal(serverFailure, handoffFailure, backendFailure, shutdown, cancellationToken);
            startupObserved = true;
            await startup;
            handoffFailure = myHandoffPump?.Failure ?? myNever;
            backendFailure = (myAgentBackend as IAgentBackendFailureSource)?.Failure ?? myNever;
            ThrowForTerminalSignal(serverFailure, handoffFailure, backendFailure, shutdown, cancellationToken);

            var close = myWindowHost.WaitForCloseAsync(cancellationToken);
            await Task.WhenAny(close, shutdown, serverFailure, handoffFailure, backendFailure, cancellation);
            if (serverFailure.IsCompleted)
            {
                serverFailure.GetAwaiter().GetResult();
            }
            if (handoffFailure.IsCompleted)
            {
                ThrowHandoffFailure(handoffFailure);
            }
            if (backendFailure.IsCompleted)
            {
                ThrowBackendFailure(backendFailure);
            }
            if (shutdown.IsCompleted)
            {
                shutdown.GetAwaiter().GetResult();
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                await close;
            }
        }
        catch (ShutdownBeforeReadyException)
        {
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            startupCancellation.Cancel();
            if (startup is not null && !startupObserved)
            {
                var startupTerminationFailure = await ObserveStartupAsync(startup, startupCancellation.Token);
                if (startupTerminationFailure is not null)
                {
                    terminationFailures.Add(startupTerminationFailure);
                }
            }
        }

        var cleanupFailures = await CleanupAsync();
        cleanupFailures = [.. terminationFailures, .. cleanupFailures];
        if (primary is not null)
        {
            ThrowWithCleanup(primary, cleanupFailures);
        }
        ThrowCleanupFailures(cleanupFailures);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = await CleanupAsync();
        ThrowCleanupFailures(failures);
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = await myLaunchPreparer.PrepareAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myAgentBackend = await myAgentProviderFactory.CreateAsync(prepared.BackendContext, cancellationToken);
        myHandoffPump = new InProcessHandoffPoller(
            prepared.HandoffRoles, myHandoffNotifier, new HandoffDeliveryLog(prepared.HandoffLogPath));
        myRuntimeController = new SquadRuntimeController(
            mySessionRegistry, myAgentBackend, myViewModel, myHandoffPump, myStopping.Token);
        cancellationToken.ThrowIfCancellationRequested();
        await mySleepInhibitor.StartAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myViewModel.InitializeRoles(prepared.RoleNames);
        myViewModel.SetLeader(prepared.Leader);
        myHostLease.SetAgentReadinessProvider(myViewModel.GetRoleReadinessAsync);
        cancellationToken.ThrowIfCancellationRequested();
        await myWindowHost.StartAsync(cancellationToken);
        myWindowStarted = true;
        cancellationToken.ThrowIfCancellationRequested();
        await myRuntimeController.StartAsync(myWindowHost.SessionsStartedAsync, cancellationToken);
    }

    private async Task<IReadOnlyList<Exception>> CleanupAsync()
    {
        lock (myCleanupLock)
            myCleanup ??= CleanupCoreAsync();
        return await myCleanup;
    }

    private async Task<IReadOnlyList<Exception>> CleanupCoreAsync()
    {
        var failures = new List<Exception>();
        myStopping.Cancel();
        if (myRuntimeController is not null)
        {
            failures.AddRange(await myRuntimeController.StopAsync());
        }
        else
        {
            // No backend was ever created (failure before or during provider selection), so there is no runtime
            // generation to tear down. Still stop the view model so any admitted commands are canceled.
            await AttemptCleanupAsync("view model stop", () => myViewModel.StopAsync(), failures);
        }

        if (myWindowStarted)
        {
            await AttemptCleanupAsync("window host stop", () => myWindowHost.StopAsync(), failures);
        }
        await AttemptCleanupAsync("window host disposal", () => myWindowHost.DisposeAsync().AsTask(), failures);
        if (myHandoffPump is not null)
        {
            await AttemptCleanupAsync("handoff pump disposal", () => myHandoffPump.DisposeAsync().AsTask(), failures);
        }
        await AttemptCleanupAsync("sleep inhibitor", () => mySleepInhibitor.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("view model", () => myViewModel.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("host lease", () => myHostLease.DisposeAsync().AsTask(), failures);
        myStopping.Dispose();
        return failures;
    }

    private static void ThrowForTerminalSignal(
        Task serverFailure,
        Task handoffFailure,
        Task backendFailure,
        Task shutdown,
        CancellationToken cancellationToken)
    {
        if (serverFailure.IsCompleted)
        {
            serverFailure.GetAwaiter().GetResult();
        }
        if (handoffFailure.IsCompleted)
        {
            ThrowHandoffFailure(handoffFailure);
        }
        if (backendFailure.IsCompleted)
        {
            ThrowBackendFailure(backendFailure);
        }
        if (shutdown.IsCompleted)
        {
            shutdown.GetAwaiter().GetResult();
            throw new ShutdownBeforeReadyException();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    // Re-labels a fatal, backend-wide IAgentBackendFailureSource failure as itself rather than letting it surface
    // through the generic startup-failure path: the backend can fail this way at any point after it is created,
    // not only during startup, so callers must be able to tell the two apart from the exception type alone.
    private static void ThrowBackendFailure(Task backendFailure)
    {
        try
        {
            backendFailure.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new AgentBackendTerminalFailureException(exception.Message, exception);
        }
    }

    // Re-labels a fatal InProcessHandoffPoller.Failure as itself rather than letting it surface through the generic
    // startup-failure path: the handoff pump can fail this way at any point after it starts, not only during
    // startup, so callers must be able to tell the two apart from the exception type alone.
    private static void ThrowHandoffFailure(Task handoffFailure)
    {
        try
        {
            handoffFailure.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new HandoffPumpTerminalFailureException(exception.Message, exception);
        }
    }

    private static async Task<Exception?> ObserveStartupAsync(Task startup, CancellationToken expectedCancellation)
    {
        try
        {
            await startup;
            return null;
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == expectedCancellation)
        {
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task AttemptCleanupAsync(string name, Func<Task> action, ICollection<Exception> failures)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void ThrowWithCleanup(ExceptionDispatchInfo primary, IReadOnlyList<Exception> cleanupFailures)
    {
        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException("Squad lifecycle failed and cleanup also failed.", [primary.SourceException, .. cleanupFailures]);
        }
        primary.Throw();
    }

    private static void ThrowCleanupFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count > 0)
        {
            throw new AggregateException("One or more squad resources failed during cleanup.", failures);
        }
    }
}

