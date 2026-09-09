using squad.Hosting.Abstractions;
using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application;
using squad.Handoffs.Delivery;
using squad.Host.Control;
using System.Runtime.ExceptionServices;

namespace squad.Host.Runtime;

/// <summary>
/// Owns the process-wide startup, running, and cleanup lifecycle, including the window, backend generation,
/// handoff pump, sleep inhibitor, and optional host lease.
/// </summary>
public sealed class SquadApplication : IAsyncDisposable
{
    private static readonly Task myNever = Task.Delay(Timeout.InfiniteTimeSpan);
    private static readonly IReadOnlyDictionary<string, IAgentSession> myEmptySessions =
        new Dictionary<string, IAgentSession>(StringComparer.Ordinal);
    private readonly SquadStartupPlan myStartupPlan;
    private readonly IAgentProviderFactory myAgentProviderFactory;
    private readonly IHandoffPump myHandoffPump;
    private readonly IWindowHost myWindowHost;
    private readonly ISleepInhibitor mySleepInhibitor;
    private readonly SquadViewModel myViewModel;
    private readonly IHostLease? myHostLease;
    private readonly SessionRegistry mySessionRegistry;
    private readonly Action<AgentEvent> myEventSink;
    private readonly CancellationTokenSource myStopping = new();
    private readonly object myCleanupLock = new();
    private IAgentBackend? myAgentBackend;
    private SquadRuntimeController? myRuntimeController;
    private Task<IReadOnlyList<Exception>>? myCleanup;
    private bool myWindowStarted;

    public SquadApplication(
        SquadStartupPlan startupPlan,
        IAgentProviderFactory agentProviderFactory,
        IHandoffPump handoffPump,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        Action<AgentEvent>? eventSink = null,
        SquadViewModel? viewModel = null,
        IHostLease? hostLease = null)
        : this(
            startupPlan,
            agentProviderFactory,
            handoffPump,
            windowHost,
            sleepInhibitor,
            new SessionRegistry(),
            eventSink,
            viewModel,
            hostLease)
    {
    }

    /// <summary>
    /// Creates an application whose handoff notifier and command dispatch share one session registry, keeping
    /// notification routing atomic with lifecycle admission.
    /// </summary>
    public static SquadApplication Create(
        SquadStartupPlan startupPlan,
        IAgentProviderFactory agentProviderFactory,
        Func<IRoleNotifier, IHandoffPump> handoffPumpFactory,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        Action<AgentEvent>? eventSink = null,
        SquadViewModel? viewModel = null,
        IHostLease? hostLease = null)
    {
        ArgumentNullException.ThrowIfNull(handoffPumpFactory);

        viewModel ??= new SquadViewModel();
        var sessionRegistry = new SessionRegistry();
        var notifier = new SessionRoleNotifier(sessionRegistry, viewModel);
        var handoffPump = handoffPumpFactory(notifier);
        return new SquadApplication(
            startupPlan,
            agentProviderFactory,
            handoffPump,
            windowHost,
            sleepInhibitor,
            sessionRegistry,
            eventSink,
            viewModel,
            hostLease);
    }

    // Internal composition seam used exclusively by Create above: lets the production creation path share one
    // SessionRegistry instance between SquadApplication and SessionRoleNotifier without exposing the
    // registry-sharing constructor as public API.
    internal SquadApplication(
        SquadStartupPlan startupPlan,
        IAgentProviderFactory agentProviderFactory,
        IHandoffPump handoffPump,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        SessionRegistry sessionRegistry,
        Action<AgentEvent>? eventSink = null,
        SquadViewModel? viewModel = null,
        IHostLease? hostLease = null)
    {
        myStartupPlan = startupPlan;
        myAgentProviderFactory = agentProviderFactory;
        myHandoffPump = handoffPump;
        myWindowHost = windowHost;
        mySleepInhibitor = sleepInhibitor;
        myViewModel = viewModel ?? new SquadViewModel();
        myHostLease = hostLease;
        mySessionRegistry = sessionRegistry;
        myEventSink = eventSink ?? (_ => { });
        myViewModel.UseAdmission(sessionRegistry);
    }

    public IReadOnlyDictionary<string, IAgentSession> Sessions => myRuntimeController?.Sessions ?? myEmptySessions;
    public SquadViewModel ViewModel => myViewModel;

    /// <summary>
    /// Runs startup through readiness, then waits for shutdown, window closure, cancellation, or an owned-resource
    /// failure. Cleanup always runs; cleanup failures are preserved alongside the primary failure.
    /// </summary>
    public async Task<RunResult> RunAsync(Func<Task> onReady, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onReady);

        ExceptionDispatchInfo? primary = null;
        RunResult? result = null;
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var shutdown = myHostLease?.ShutdownRequested ?? myNever;
        var serverFailure = myHostLease?.ServerFailure ?? myNever;
        var handoffFailure = myHandoffPump.Failure;
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
            backendFailure = (myAgentBackend as IAgentBackendFailureSource)?.Failure ?? myNever;
            ThrowForTerminalSignal(serverFailure, handoffFailure, backendFailure, shutdown, cancellationToken);

            await onReady();

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
                result = RunResult.StoppedAfterReady;
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                await close;
                result = RunResult.StoppedAfterReady;
            }
        }
        catch (ShutdownBeforeReadyException)
        {
            result = RunResult.ShutdownBeforeReady;
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
        return result ?? RunResult.ShutdownBeforeReady;
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
        var backendContext = await myStartupPlan.PrepareContextAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myAgentBackend = await myAgentProviderFactory.CreateAsync(backendContext, cancellationToken);
        myRuntimeController = new SquadRuntimeController(
            mySessionRegistry, myAgentBackend, myEventSink, myViewModel, myHandoffPump, myStopping.Token);
        cancellationToken.ThrowIfCancellationRequested();
        await mySleepInhibitor.StartAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myViewModel.InitializeRoles(myStartupPlan.DiscoverRoles());
        myHostLease?.SetAgentReadinessProvider(myViewModel.GetRoleReadinessAsync);
        myStartupPlan.PrepareWorkspace();
        cancellationToken.ThrowIfCancellationRequested();
        await myStartupPlan.PrepareConfiguredWorktreesForLaunchAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myStartupPlan.PrepareHandoffDirs();
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
        await AttemptCleanupAsync("handoff pump disposal", () => myHandoffPump.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("sleep inhibitor", () => mySleepInhibitor.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("view model", () => myViewModel.DisposeAsync().AsTask(), failures);
        if (myHostLease is not null)
        {
            await AttemptCleanupAsync("host lease", () => myHostLease.DisposeAsync().AsTask(), failures);
        }
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

    // Re-labels a fatal IHandoffPump.Failure as itself rather than letting it surface through the generic
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

