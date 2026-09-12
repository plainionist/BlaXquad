using squad.Hosting.Abstractions;
using squad.AgentProvider.Abstractions;
using squad.Application;
using squad.Application.Transcripts;
using squad.Domain;
using squad.Runtime.Control;
using squad.Ui.Abstractions;
using squad.Workspaces;
using System.Runtime.ExceptionServices;

namespace squad.Runtime;

/// <summary>
/// The process shell. Headquarters owns the resources whose lifetime is the process - the project lease and
/// control endpoint, the window and UI transport, sleep inhibition, the durable workspace services, and the
/// transcript archive - plus one serialized active-squad slot. It never owns a backend, a session, a member
/// processor, an interaction, or handoff participation: every resource whose lifetime follows a generation belongs
/// to the installed <see cref="Squad"/> and is released only through <see cref="Squad.RetireAsync"/>.
///
/// Installation, replacement, and the final stop all pass through the same gate, so at most one generation exists
/// at a time and a new one never starts over a retirement that is not conclusive.
/// </summary>
public sealed class Headquarters : IAsyncDisposable
{
    private static readonly Task myNever = Task.Delay(Timeout.InfiniteTimeSpan);
    private readonly LaunchPreparer myLaunchPreparer;
    private readonly IAgentProviderFactory myAgentProviderFactory;
    private readonly IWindowHost myWindowHost;
    private readonly ISleepInhibitor mySleepInhibitor;
    private readonly SquadViewModel myViewModel;
    private readonly IWorkspaceTools myWorkspaceTools;
    private readonly HeadquartersLease myHeadquartersLease;
    private readonly TranscriptStore myTranscripts = new();
    // The active-squad slot. Every installation, replacement, and retirement is serialized through this gate, so
    // two generations can never overlap and a stop can never race an installation.
    private readonly SemaphoreSlim mySlotGate = new(1, 1);
    private readonly object myCleanupLock = new();
    private Squad? mySquad;
    private Task<IReadOnlyList<Exception>>? myCleanup;
    private bool myWindowStarted;
    // Set once, only while collecting the process's own cleanup failures. While Headquarters keeps running (a
    // replacement, or a failed installation's own retry-on-catch), a conclusive retirement that empties the slot
    // also uninstalls that generation from the process-lifetime UI port, so it answers as an empty squad instead of
    // still publishing a generation nothing owns anymore. During process-wide stop the retired generation stays
    // installed instead, so a query in flight still sees a known, not-ready role rather than an unknown one.
    private bool myStopping;

    /// <summary>
    /// Creates Headquarters for production use. This is the single production composition path.
    /// </summary>
    public static Headquarters Create(
        LaunchPreparer launchPreparer,
        IAgentProviderFactory agentProviderFactory,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        SquadViewModel viewModel,
        IWorkspaceTools workspaceTools,
        HeadquartersLease headquartersLease)
    {
        return new Headquarters(
            launchPreparer,
            agentProviderFactory,
            windowHost,
            sleepInhibitor,
            viewModel,
            workspaceTools,
            headquartersLease);
    }

    private Headquarters(
        LaunchPreparer launchPreparer,
        IAgentProviderFactory agentProviderFactory,
        IWindowHost windowHost,
        ISleepInhibitor sleepInhibitor,
        SquadViewModel viewModel,
        IWorkspaceTools workspaceTools,
        HeadquartersLease headquartersLease)
    {
        myLaunchPreparer = launchPreparer;
        myAgentProviderFactory = agentProviderFactory;
        myWindowHost = windowHost;
        mySleepInhibitor = sleepInhibitor;
        myViewModel = viewModel;
        myWorkspaceTools = workspaceTools;
        myHeadquartersLease = headquartersLease;
    }

    /// <summary>
    /// Runs startup through readiness, then waits for shutdown, window closure, cancellation, or an owned-resource
    /// failure. Cleanup always runs; cleanup failures are preserved alongside the primary failure.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ExceptionDispatchInfo? primary = null;
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var shutdown = myHeadquartersLease.ShutdownRequested;
        var serverFailure = myHeadquartersLease.ServerFailure;
        // No squad generation exists until startup reaches its installation after process preparation, so no fatal
        // handoff or backend signal can fire before then; both are recomputed from the now-installed generation
        // once the startup task has fully completed.
        var handoffFailure = myNever;
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
            handoffFailure = mySquad?.HandoffFailure ?? myNever;
            backendFailure = mySquad?.BackendFailure ?? myNever;
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

    /// <summary>
    /// Retires the installed generation and installs a started replacement in its place, under the same gate that
    /// serializes the initial installation and the final stop. A retirement that is not conclusive keeps its
    /// generation owned and refuses the replacement; a replacement that fails to start leaves the slot empty and
    /// retryable without touching any process resource. Nothing triggers this yet - the restart issue is what
    /// exposes it - but it moves no resource that this slice has not already given to <see cref="Squad"/>.
    /// </summary>
    public async Task ReplaceSquadAsync(CancellationToken cancellationToken = default)
    {
        await mySlotGate.WaitAsync(cancellationToken);
        try
        {
            var retirement = await RetireSquadUnlockedAsync();
            if (retirement.Count > 0)
            {
                throw new AggregateException("The squad could not be retired conclusively.", retirement);
            }
            await InstallSquadUnlockedAsync(cancellationToken);
        }
        finally
        {
            mySlotGate.Release();
        }
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
        await myLaunchPreparer.PrepareProcessAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await mySleepInhibitor.StartAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await mySlotGate.WaitAsync(cancellationToken);
        try
        {
            await InstallSquadUnlockedAsync(cancellationToken);
        }
        finally
        {
            mySlotGate.Release();
        }
    }

    /// <summary>
    /// Prepares, creates, publishes, and starts one generation into the empty active-squad slot. The generation is
    /// owned from the moment it exists, so a failure part way through startup still retires exactly what was
    /// created; a conclusive retirement then leaves the slot empty and retryable.
    /// </summary>
    private async Task InstallSquadUnlockedAsync(CancellationToken cancellationToken)
    {
        Contract.Invariant(mySquad is null, "Installing into a non-empty active-squad slot.");
        var prepared = await myLaunchPreparer.PrepareGenerationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        myWorkspaceTools.Configure(prepared.GitHistoryCommand);
        var agentBackend = await myAgentProviderFactory.CreateAsync(prepared.BackendContext, cancellationToken);
        var members = new SquadMembers(
            SquadGenerationId.New(),
            prepared.Definition,
            myTranscripts,
            myViewModel);
        var squad = new Squad(
            members,
            agentBackend,
            prepared.Definition.Members,
            prepared.HandoffLogPath,
            myWindowHost.SessionsStartedAsync);
        mySquad = squad;
        try
        {
            // The generation must be published before its sessions start, so an operator that reaches Headquarters
            // during startup observes a known, not-yet-ready role rather than an unknown one.
            myViewModel.Install(members);
            myHeadquartersLease.SetAgentReadinessProvider(
                (memberId, cancellationToken) => myViewModel.GetRoleReadinessAsync(memberId.Value, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureWindowStartedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await squad.StartAsync(cancellationToken);
        }
        catch
        {
            await RetireSquadUnlockedAsync();
            throw;
        }
    }

    /// <summary>
    /// Retires the installed generation, if any, and empties the slot when that retirement is conclusive. A
    /// non-conclusive retirement keeps the generation owned, so no replacement can be installed over it. While
    /// Headquarters keeps running, a conclusive retirement also uninstalls that generation from the process-
    /// lifetime UI port, keeping it in lockstep with the now-empty slot; during process-wide stop it stays
    /// installed instead, matching the existing shutdown-admission behavior. The collected failures are reported
    /// exactly once, by whichever caller first retires the generation.
    /// </summary>
    private async Task<IReadOnlyList<Exception>> RetireSquadUnlockedAsync()
    {
        if (mySquad is null)
        {
            return [];
        }
        var generation = mySquad.Generation;
        var retirement = await mySquad.RetireAsync();
        if (retirement.IsConclusive)
        {
            mySquad = null;
            if (!myStopping)
            {
                myViewModel.Uninstall(generation);
            }
        }
        return retirement.Failures;
    }

    /// <summary>Starts the window exactly once, no matter how many generations are installed behind it.</summary>
    private async Task EnsureWindowStartedAsync(CancellationToken cancellationToken)
    {
        if (myWindowStarted)
        {
            return;
        }
        await myWindowHost.StartAsync(cancellationToken);
        myWindowStarted = true;
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
        // Close process-level command admission first, so a command is rejected even when no generation was ever
        // installed to reject it. Marking the process as stopping before retiring keeps a retired generation
        // installed for the rest of process-wide cleanup, matching existing shutdown-admission behavior, instead
        // of uninstalling it as a mid-run replacement would.
        myViewModel.BeginStopping();
        myStopping = true;
        await mySlotGate.WaitAsync();
        try
        {
            failures.AddRange(await RetireSquadUnlockedAsync());
        }
        finally
        {
            mySlotGate.Release();
        }

        if (myWindowStarted)
        {
            await AttemptCleanupAsync("window host stop", () => myWindowHost.StopAsync(), failures);
        }
        await AttemptCleanupAsync("window host disposal", () => myWindowHost.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("sleep inhibitor", () => mySleepInhibitor.DisposeAsync().AsTask(), failures);
        await AttemptCleanupAsync("transcript archive", () =>
        {
            myTranscripts.Dispose();
            return Task.CompletedTask;
        }, failures);
        await AttemptCleanupAsync("Headquarters lease", () => myHeadquartersLease.DisposeAsync().AsTask(), failures);
        mySlotGate.Dispose();
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
