using squad.Hosting.Abstractions;
using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application;
using squad.Handoffs.Delivery;
using System.Runtime.ExceptionServices;

namespace squadHQ.Commands;

public sealed class SquadApplication : IAsyncDisposable
{
    private static readonly Task myNever = Task.Delay(Timeout.InfiniteTimeSpan);
    private readonly Ctx myCtx;
    private readonly WorkspacePreparer myWorkspacePreparer;
    private readonly IAgentBackend myAgentBackend;
    private readonly IHandoffPump myHandoffPump;
    private readonly IWindowHost myWindowHost;
    private readonly ISleepInhibitor mySleepInhibitor;
    private readonly SquadViewModel myViewModel;
    private readonly IHostLease? myHostLease;
    private readonly Func<CancellationToken, Task>? myPostLockPreparation;
    private readonly SquadRuntimeController myRuntimeController;
    private readonly CancellationTokenSource myStopping = new();
    private readonly object myCleanupLock = new();
    private Task<IReadOnlyList<Exception>>? myCleanup;
    private bool myWindowStarted;

    public SquadApplication(
        Ctx ctx,
        WorkspacePreparer workspacePreparer,
        IAgentBackend agentBackend,
        IHandoffPump handoffPump,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        Action<AgentEvent>? eventSink = null,
        SquadViewModel? viewModel = null,
        IHostLease? hostLease = null,
        Func<CancellationToken, Task>? postLockPreparation = null)
        : this(
            ctx,
            workspacePreparer,
            agentBackend,
            handoffPump,
            windowHost,
            sleepInhibitor,
            new SessionRegistry(),
            eventSink,
            viewModel,
            hostLease,
            postLockPreparation)
    {
    }

    // Internal composition seam: lets headquarters and test support share one SessionRegistry instance between
    // SquadApplication and SessionRoleNotifier without exposing the host-lifecycle type on the public constructor.
    internal SquadApplication(
        Ctx ctx,
        WorkspacePreparer workspacePreparer,
        IAgentBackend agentBackend,
        IHandoffPump handoffPump,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        SessionRegistry sessionRegistry,
        Action<AgentEvent>? eventSink = null,
        SquadViewModel? viewModel = null,
        IHostLease? hostLease = null,
        Func<CancellationToken, Task>? postLockPreparation = null)
    {
        myCtx = ctx;
        myWorkspacePreparer = workspacePreparer;
        myAgentBackend = agentBackend;
        myHandoffPump = handoffPump;
        myWindowHost = windowHost;
        mySleepInhibitor = sleepInhibitor;
        myViewModel = viewModel ?? new SquadViewModel();
        myHostLease = hostLease;
        myPostLockPreparation = postLockPreparation;
        myViewModel.UseAdmission(sessionRegistry);
        myRuntimeController = new SquadRuntimeController(
            sessionRegistry, myAgentBackend, eventSink ?? (_ => { }), myViewModel, myHandoffPump, myStopping.Token);
    }

    public IReadOnlyDictionary<string, IAgentSession> Sessions => myRuntimeController.Sessions;
    public SquadViewModel ViewModel => myViewModel;

    public async Task<RunResult> RunAsync(Func<Task> onReady, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onReady);

        ExceptionDispatchInfo? primary = null;
        RunResult? result = null;
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var shutdown = myHostLease?.ShutdownRequested ?? myNever;
        var serverFailure = myHostLease?.ServerFailure ?? myNever;
        var handoffFailure = myHandoffPump.Failure;
        var backendFailure = (myAgentBackend as IAgentBackendFailureSource)?.Failure ?? myNever;
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
            ThrowForTerminalSignal(serverFailure, handoffFailure, backendFailure, shutdown, cancellationToken);

            await onReady();

            var close = myWindowHost.WaitForCloseAsync(cancellationToken);
            await Task.WhenAny(close, shutdown, serverFailure, handoffFailure, backendFailure, cancellation);
            if (serverFailure.IsCompleted)
                serverFailure.GetAwaiter().GetResult();
            if (handoffFailure.IsCompleted)
                handoffFailure.GetAwaiter().GetResult();
            if (backendFailure.IsCompleted)
                backendFailure.GetAwaiter().GetResult();
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
                    terminationFailures.Add(startupTerminationFailure);
            }
        }

        var cleanupFailures = await CleanupAsync();
        cleanupFailures = [.. terminationFailures, .. cleanupFailures];
        if (primary is not null)
            ThrowWithCleanup(primary, cleanupFailures);
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
        if (myPostLockPreparation is not null)
            await myPostLockPreparation(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await mySleepInhibitor.StartAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myViewModel.InitializeRoles(myCtx.Roles.Select(role => role.Role));
        myHostLease?.SetAgentReadinessProvider(myViewModel.GetRoleReadinessAsync);
        myWorkspacePreparer.PrepareWorkspace(myCtx);
        cancellationToken.ThrowIfCancellationRequested();
        await myWorkspacePreparer.PrepareConfiguredWorktreesForLaunchAsync(myCtx, myCtx.ContinueLaunch, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myWorkspacePreparer.PrepareHandoffDirs(myCtx);
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
        failures.AddRange(await myRuntimeController.StopAsync());

        if (myWindowStarted)
            await AttemptCleanupAsync("window host stop", () => myWindowHost.StopAsync(), failures);
        await AttemptCleanupAsync("window host disposal", () => myWindowHost.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("handoff pump disposal", () => myHandoffPump.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("sleep inhibitor", () => mySleepInhibitor.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("view model", () => myViewModel.DisposeAsync().AsTask(), failures);
        if (myHostLease is not null)
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
            serverFailure.GetAwaiter().GetResult();
        if (handoffFailure.IsCompleted)
            handoffFailure.GetAwaiter().GetResult();
        if (backendFailure.IsCompleted)
            backendFailure.GetAwaiter().GetResult();
        if (shutdown.IsCompleted)
        {
            shutdown.GetAwaiter().GetResult();
            throw new ShutdownBeforeReadyException();
        }
        cancellationToken.ThrowIfCancellationRequested();
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
            throw new AggregateException("Squad lifecycle failed and cleanup also failed.", [primary.SourceException, .. cleanupFailures]);
        primary.Throw();
    }

    private static void ThrowCleanupFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count > 0)
            throw new AggregateException("One or more squad resources failed during cleanup.", failures);
    }
}



