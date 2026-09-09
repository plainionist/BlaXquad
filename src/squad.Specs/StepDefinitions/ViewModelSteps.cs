using squad.Specs.Support;
using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Process;
using squad.Configuration;
using squad.CopilotSdk;
using squad.Application;
using squad.Transcripts;
using squad.Ui.Protocol;
using squad.Ui.Abstractions;
using squadHQ.Commands;
using squad.Workspaces;
using squad.Host.Control;
using squad.Host.Runtime;
using System.Text.Json;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ViewModelSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private SquadViewModel myViewModel = null!;
    private RecordingAgentBackend myBackend = null!;
    private SquadApplication? myApplication;
    private string myApplicationRoot = "";
    private RecordingWindowHost? myRecordingWindow;
    private Exception? myApplicationStartFailure;
    private HostLease? myApplicationLease;
    private Ctx? myApplicationContext;
    private Task<RunResult>? myApplicationRun;
    private int myApplicationReadyCount;
    private RecordingHandoffPump? myRecordingPump;
    private RecordingSleepInhibitor? myRecordingSleep;
    private RecordingHostLease? myRecordingHostLease;
    private FaultingHostLease? myFaultingHostLease;
    private Task? myStoppingCommand;
    private Task? myInFlightApplicationCommand;
    private Task? myExternalShutdown;
    private TaskCompletionSource? myPreparationEntered;
    private TaskCompletionSource? myPreparationGate;
    private TaskCompletionSource? myPreparationCanceled;
    private RunResult? myApplicationRunResult;
    private Exception? myApplicationLifecycleFailure;
    private readonly List<string> mySdkInstructionsSentAfterRegistration = [];
    private readonly List<TranscriptUpdate> myTranscriptUpdates = [];
    private readonly CopilotToolOutputNormalizer myToolOutputNormalizer = new();
    private readonly Dictionary<string, string> myActiveToolCallIds = new(StringComparer.Ordinal);
    private int myNextToolCallId;
    private LifecycleTrace? myLifecycleTrace;

    public ViewModelSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a ViewModel with recording roles {string}")]
    public void GivenAViewModelWithRecordingRoles(string roles)
    {
        myViewModel = new SquadViewModel();
        myViewModel.TranscriptChanged += myTranscriptUpdates.Add;
        myBackend = new RecordingAgentBackend();
        var roleNames = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myViewModel.InitializeRoles(roleNames);
        foreach (var role in roleNames)
        {
            myBackend.AddRole(role);
            myViewModel.RegisterSession(myBackend.Sessions.Single(session => session.Role == role));
        }
    }

    [Given("a SquadApplication with recording roles {string}")]
    public void GivenASquadApplicationWithRecordingRoles(string roles)
    {
        var roleNames = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myApplicationRoot = Path.Combine(Path.GetTempPath(), "blaxquad-viewmodel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(myApplicationRoot, "blaxquad", "roles"));
        File.WriteAllText(Path.Combine(myApplicationRoot, "blaxquad", "constitution.prompt"), "Follow the constitution.\n");
        foreach (var role in roleNames)
        {
            File.WriteAllText(Path.Combine(myApplicationRoot, "blaxquad", "roles", role + ".prompt"), "Follow the role.\n");
        }

        myBackend = new RecordingAgentBackend();
        foreach (var role in roleNames)
        {
            myBackend.AddRole(role);
        }

        var ctx = new Ctx
        {
            WorkingDir = myApplicationRoot,
            ScriptDir = AppContext.BaseDirectory.TrimEnd('/', '\\'),
            ContinueLaunch = true,
            Roles = roleNames.Select(role => new RoleConfigRow(role, role, "master", myApplicationRoot, "task")).ToList(),
        };
        ctx.ConfigFile = Path.Combine(myApplicationRoot, "blaxquad", "squad.json");
        ctx.RolesDir = Path.Combine(myApplicationRoot, "blaxquad", "roles");
        ctx.ConstitutionFile = Path.Combine(myApplicationRoot, "blaxquad", "constitution.prompt");
        ctx.StateDir = Path.Combine(myApplicationRoot, ".blaxquad");
        ctx.WorktreesDir = Path.Combine(myApplicationRoot, ".worktrees");
        ctx.HandoffLog = Path.Combine(ctx.StateDir, "handoff-delivery.log");

        var viewModel = new SquadViewModel();
        myApplicationContext = ctx;
        myRecordingWindow = new RecordingWindowHost();
        myRecordingPump = new RecordingHandoffPump();
        myRecordingSleep = new RecordingSleepInhibitor();
        myApplication = SquadApplication.Create(
            SquadStartupPlanFactory.ForWorkspace(ctx, new WorkspacePreparer(_ => { })),
            new RecordingAgentProviderFactory(myBackend),
            handoffPumpFactory: _ => myRecordingPump,
            myRecordingWindow,
            myRecordingSleep,
            viewModel: viewModel);
    }

    [Given("a SquadApplication with a session that emits while shutting down")]
    public void GivenASquadApplicationWithASessionThatEmitsWhileShuttingDown()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        var session = myBackend.Sessions.Single();
        session.IgnoreEventCancellation = true;
        session.OnDispose = () => session.Emit(new AgentStartedEvent(DateTimeOffset.UtcNow));
    }

    [Given("a SquadApplication with recording roles {string} and a lifecycle trace whose backend fails during startup")]
    public void GivenASquadApplicationWithRecordingRolesAndALifecycleTraceWhoseBackendFailsDuringStartup(string roles)
    {
        GivenASquadApplicationWithRecordingRoles(roles);
        WireLifecycleTrace();
        myBackend.FailAfterCreatingSessionCount = 1;
        // Inject the cleanup failure in a generation-scoped teardown step (the registered session's own disposal),
        // which precedes backend, window, handoff pump, and sleep inhibitor cleanup, so the scenario proves that
        // failure cannot skip the mandatory process-wide release that follows it.
        myBackend.Sessions.Single(session => session.Role == "coder").FailOnDispose = true;
    }

    private void WireLifecycleTrace()
    {
        myLifecycleTrace = new LifecycleTrace();
        myRecordingWindow!.Trace = myLifecycleTrace;
        myBackend.Trace = myLifecycleTrace;
        myRecordingPump!.Trace = myLifecycleTrace;
        myRecordingSleep!.Trace = myLifecycleTrace;
        foreach (var session in myBackend.Sessions)
        {
            session.Trace = myLifecycleTrace;
        }
    }

    [Given("a SquadApplication with recording roles and a host lease")]
    public void GivenASquadApplicationWithRecordingRolesAndAHostLease()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        myApplicationLease = HostLease.Acquire(myApplicationRoot);
        myApplication = new SquadApplication(SquadStartupPlanFactory.ForWorkspace(myApplicationContext!, new WorkspacePreparer(_ => { })), new RecordingAgentProviderFactory(myBackend), myRecordingPump!, myRecordingWindow!, myRecordingSleep!, viewModel: myApplication!.ViewModel, hostLease: myApplicationLease);
    }

    [When("the leased SquadApplication starts")]
    public async Task WhenTheLeasedSquadApplicationStarts() => await StartApplicationUntilReadyAsync();

    [When("an external client requests application shutdown")]
    public async Task WhenAnExternalClientRequestsApplicationShutdown()
    {
        await BeginExternalShutdownAsync();
        await myExternalShutdown!;
    }

    [When("an external client begins requesting application shutdown")]
    public async Task WhenAnExternalClientBeginsRequestingApplicationShutdown() => await BeginExternalShutdownAsync();

    [When("the lease-owned application lifecycle runs")]
    public async Task WhenTheLeaseOwnedApplicationLifecycleRuns() => await RunApplicationToCompletionAsync();

    [Given("a lease-owned SquadApplication with blocked preparation")]
    public void GivenALeaseOwnedSquadApplicationWithBlockedPreparation()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        myApplicationLease = HostLease.Acquire(myApplicationRoot);
        myPreparationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        myPreparationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        myPreparationCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        myApplication = new SquadApplication(
            SquadStartupPlanFactory.ForWorkspace(
                myApplicationContext!,
                new WorkspacePreparer(_ => { }),
                prepareContextAsync: async cancellationToken =>
                {
                    myPreparationEntered.TrySetResult();
                    try
                    {
                        await myPreparationGate.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        myPreparationCanceled.TrySetResult();
                        throw;
                    }
                    return BuildAgentBackendContext(myApplicationContext!);
                }),
            new RecordingAgentProviderFactory(myBackend),
            myRecordingPump!,
            myRecordingWindow!,
            myRecordingSleep!,
            viewModel: myApplication!.ViewModel,
            hostLease: myApplicationLease);
    }

    [When("the lease-owned application lifecycle begins preparation")]
    public async Task WhenTheLeaseOwnedApplicationLifecycleBeginsPreparation()
    {
        StartApplicationRun();
        await myPreparationEntered!.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Then("the lease-owned application resources are released")]
    public void ThenTheLeaseOwnedApplicationResourcesAreReleased()
    {
        myExternalShutdown?.GetAwaiter().GetResult();
        Assert.Multiple(() =>
        {
            Assert.That(myApplicationRun!.IsCompletedSuccessfully, Is.True);
            Assert.That(File.Exists(Path.Combine(myApplicationRoot, ".blaxquad", "host.json")), Is.False);
            Assert.That(HostLease.TryAcquireProbe(myApplicationRoot), Is.True);
            // The backend runtime is only created once startup reaches session generation; if the
            // application stopped before then, no runtime exists and nothing was ever owned/disposed.
            Assert.That(myBackend.Disposed, Is.EqualTo(myBackend.RuntimeCreated));
            Assert.That(myRecordingWindow!.DisposeCount, Is.EqualTo(1));
            Assert.That(myRecordingPump!.Disposed, Is.True);
            Assert.That(myRecordingSleep!.Disposed, Is.True);
        });
    }

    [When("the SquadApplication starts")]
    public async Task WhenTheSquadApplicationStarts() => await StartApplicationUntilReadyAsync();

    [Given("a SquadApplication that fails before window startup")]
    public void GivenASquadApplicationThatFailsBeforeWindowStartup()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        AttachHostLease();
        myRecordingWindow!.FailOnStart = true;
    }

    [Given("a SquadApplication with a CLI startup failure")]
    public void GivenASquadApplicationWithACliStartupFailure()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        AttachHostLease();
        myApplication = new SquadApplication(
            SquadStartupPlanFactory.ForWorkspace(
                myApplicationContext!,
                new WorkspacePreparer(_ => { }),
                prepareContextAsync: _ => throw new CliExitException(1, "recording CLI startup failure")),
            new RecordingAgentProviderFactory(myBackend),
            myRecordingPump!,
            myRecordingWindow!,
            myRecordingSleep!,
            viewModel: myApplication!.ViewModel,
            hostLease: myApplicationLease);
    }

    [Given("a SquadApplication that fails after window startup")]
    public void GivenASquadApplicationThatFailsAfterWindowStartup()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        AttachHostLease();
        myRecordingWindow!.FailOnSessionsStarted = true;
    }

    [Given("a SquadApplication whose backend fails during startup")]
    public void GivenASquadApplicationWhoseBackendFailsDuringStartup()
    {
        GivenASquadApplicationWithRecordingRoles("coder,reviewer");
        AttachHostLease();
        myBackend.FailAfterCreatingSessionCount = 1;
    }

    [Given("a SquadApplication with SDK-shaped recording roles {string}")]
    public void GivenASquadApplicationWithSdkShapedRecordingRoles(string roles)
    {
        GivenASquadApplicationWithRecordingRoles(roles);
        var roleNames = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sdkRoles = roleNames.Select((role, index) =>
        {
            var worktreePath = Path.Combine(myApplicationRoot, ".worktrees", role);
            Directory.CreateDirectory(Path.Combine(worktreePath, ".git"));
            return new RoleConfigRow(role, role, $"sdk-{index + 1}", worktreePath, "task");
        }).ToList();
        myApplicationContext!.Roles = sdkRoles;
        Directory.CreateDirectory(myApplicationContext.StateDir);
        myBackend = new RecordingAgentBackend();
        mySdkInstructionsSentAfterRegistration.Clear();
        foreach (var role in sdkRoles)
        {
            myBackend.AddSdkRole(role, new AgentStartedEvent(DateTimeOffset.UtcNow), $"Initial instructions for {role.Role}.");
            var session = myBackend.Sessions.Single(candidate => candidate.Role == role.Role);
            session.OnSend = _ =>
            {
                if (myApplication!.Sessions.ContainsKey(role.Role))
                {
                    mySdkInstructionsSentAfterRegistration.Add(role.Role);
                }
            };
        }

        var viewModel = myApplication!.ViewModel;
        myApplication = new SquadApplication(
            SquadStartupPlanFactory.ForWorkspace(myApplicationContext, new WorkspacePreparer(_ => { })),
            new RecordingAgentProviderFactory(myBackend),
            myRecordingPump!,
            myRecordingWindow!,
            myRecordingSleep!,
            viewModel: viewModel);
    }

    [Given("the SDK-shaped backend fails after its first session")]
    public void GivenTheSdkShapedBackendFailsAfterItsFirstSession() => myBackend.FailAfterCreatingSessionCount = 1;

    [When("the application start fails")]
    public async Task WhenTheApplicationStartFails()
    {
        try
        {
            await myApplication!.RunAsync(() => Task.CompletedTask);
        }
        catch (Exception exception)
        {
            myApplicationStartFailure = exception;
        }
    }

    [Then("the application start failed")]
    public void ThenTheApplicationStartFailed() => Assert.That(myApplicationStartFailure, Is.Not.Null);

    [Then("the application start failed with a CLI exit exception")]
    public void ThenTheApplicationStartFailedWithACliExitException() =>
        Assert.That(myApplicationStartFailure, Is.TypeOf<CliExitException>());

    [Then("the application cleaned up its startup resources")]
    public void ThenTheApplicationCleanedUpItsStartupResources()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myApplication!.Sessions, Is.Empty);
            Assert.That(myRecordingWindow!.StopCount, Is.LessThanOrEqualTo(1));
            Assert.That(myApplicationLease is null || HostLease.TryAcquireProbe(myApplicationRoot), Is.True);
            // The backend runtime is only ever created once startup reaches session generation; if the
            // failure happened earlier, no runtime was created and no sessions were ever owned/disposed.
            var expectDisposed = myBackend.RuntimeCreated;
            Assert.That(myBackend.Sessions, Is.All.Matches<RecordingAgentSession>(session => session.Disposed == expectDisposed));
        });
    }

    [Then("SDK-shaped sessions use distinct role worktrees")]
    public void ThenSdkShapedSessionsUseDistinctRoleWorktrees()
    {
        var worktrees = myBackend.RoleWorktrees.Values.Distinct(StringComparer.Ordinal).ToList();
        Assert.That(worktrees, Has.Count.EqualTo(myBackend.Sessions.Count));
    }

    [Then("early SDK-shaped events reached each ViewModel role")]
    public void ThenEarlySdkShapedEventsReachedEachViewModelRole()
    {
        var application = myApplication!;
        myWorkspace.WaitUntil(() => application.ViewModel.Roles.Values.All(role => role.EventCount == 1), "early SDK-shaped events");
        Assert.That(application.ViewModel.Roles.Values, Is.All.Matches<AgentRoleState>(role => role.Status == "running"));
    }

    [Then("SDK-shaped initial instructions were sent after session registration")]
    public void ThenSdkShapedInitialInstructionsWereSentAfterSessionRegistration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(mySdkInstructionsSentAfterRegistration, Is.EquivalentTo(myBackend.Sessions.Select(session => session.Role)));
            Assert.That(myBackend.Sessions.Select(session => session.Sends.Single()), Is.EqualTo(myBackend.Sessions.Select(session => $"Initial instructions for {session.Role}.")));
        });
    }

    [Then("SDK-shaped sessions were disposed in reverse registration order")]
    public void ThenSdkShapedSessionsWereDisposedInReverseRegistrationOrder() =>
        Assert.That(myBackend.DisposeOrder, Is.EqualTo(myBackend.Sessions.Select(session => session.Role).Reverse()));

    [Then("all SDK-shaped sessions were disposed")]
    public void ThenAllSdkShapedSessionsWereDisposed() =>
        Assert.That(myBackend.Sessions, Is.All.Matches<RecordingAgentSession>(session => session.Disposed));

    [Then("the partial startup observer observed cancellation")]
    public void ThenThePartialStartupObserverObservedCancellation() =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == "coder").EventCancellationObserved, Is.True);

    [Then("the window host start was attempted")]
    public void ThenTheWindowHostStartWasAttempted() => Assert.That(myRecordingWindow!.StartCount, Is.EqualTo(1));

    [Then("the window host was stopped")]
    public void ThenTheWindowHostWasStopped() => Assert.That(myRecordingWindow!.StopCount, Is.EqualTo(1));

    [Then("the recording backend was disposed")]
    public void ThenTheRecordingBackendWasDisposed() => Assert.That(myBackend.Disposed, Is.True);

    [Given("a controllable SquadApplication with shutdown already requested")]
    public void GivenAControllableSquadApplicationWithShutdownAlreadyRequested()
    {
        ConfigureControllableApplication();
        myRecordingHostLease!.RequestShutdown();
    }

    [Given("a controllable SquadApplication")]
    public void GivenAControllableSquadApplication() => ConfigureControllableApplication();

    [Given("a controllable SquadApplication with a post-ready handoff failure")]
    public void GivenAControllableSquadApplicationWithAPostReadyHandoffFailure() => ConfigureControllableApplication(useRealLease: true);

    [Given("a controllable SquadApplication with blocked startup")]
    public void GivenAControllableSquadApplicationWithBlockedStartup() => ConfigureControllableApplication(blockStartup: true);

    [Given("a controllable SquadApplication with blocked startup and a faulting server")]
    public void GivenAControllableSquadApplicationWithBlockedStartupAndAFaultingServer() =>
        ConfigureControllableApplication(blockStartup: true, faultServer: true);

    [Given("a controllable SquadApplication that requests shutdown when ready")]
    public void GivenAControllableSquadApplicationThatRequestsShutdownWhenReady()
    {
        ConfigureControllableApplication(useRealLease: true);
        myRecordingWindow!.OnSessionsStarted = () =>
            HostControlClient.RequestShutdownAsync(myApplicationRoot).GetAwaiter().GetResult();
    }

    [Given("a controllable SquadApplication with a session disposal failure and open events")]
    public void GivenAControllableSquadApplicationWithASessionDisposalFailureAndOpenEvents()
    {
        ConfigureControllableApplication(useRealLease: true);
        var session = myBackend.Sessions.Single();
        session.FailOnDispose = true;
        session.LeaveEventsOpenOnDispose = true;
    }

    [Given("a controllable SquadApplication with blocking backend cleanup")]
    public void GivenAControllableSquadApplicationWithBlockingBackendCleanup()
    {
        ConfigureControllableApplication(useRealLease: true);
        myBackend.BlockDispose = true;
    }

    [Given("a controllable SquadApplication that sends a command while stopping")]
    public void GivenAControllableSquadApplicationThatSendsACommandWhileStopping()
    {
        ConfigureControllableApplication(useRealLease: true);
        myBackend.Sessions.Single().OnDispose = () => myStoppingCommand = myApplication!.ViewModel.SendAsync("coder", "too late");
    }

    [Given("a controllable SquadApplication with an in-flight command")]
    public void GivenAControllableSquadApplicationWithAnInFlightCommand()
    {
        ConfigureControllableApplication(useRealLease: true);
        myBackend.Sessions.Single().SendDelay = TimeSpan.FromSeconds(30);
    }

    [Given("a controllable SquadApplication with startup and cleanup failures")]
    public void GivenAControllableSquadApplicationWithStartupAndCleanupFailures()
    {
        ConfigureControllableApplication(useRealLease: true);
        myRecordingWindow!.FailOnStart = true;
        myRecordingPump!.FailOnDispose = true;
    }

    [Given("a controllable SquadApplication with runtime and cleanup failures")]
    public void GivenAControllableSquadApplicationWithRuntimeAndCleanupFailures()
    {
        ConfigureControllableApplication(useRealLease: true);
        myRecordingWindow!.FailOnClose = true;
        myRecordingPump!.FailOnDispose = true;
    }

    [Given("a controllable SquadApplication with a cancellation-failing startup and a faulting server")]
    public void GivenAControllableSquadApplicationWithACancellationFailingStartupAndAFaultingServer()
    {
        ConfigureControllableApplication(blockStartup: true, faultServer: true);
        myRecordingSleep!.FailWhenCanceled = true;
    }

    [When("the application lifecycle runs")]
    public async Task WhenTheApplicationLifecycleRuns() => await RunApplicationToCompletionAsync();

    [When("the application lifecycle begins")]
    public async Task WhenTheApplicationLifecycleBegins()
    {
        StartApplicationRun();
        await myRecordingSleep!.StartEntered.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [When("the controllable host requests shutdown")]
    public void WhenTheControllableHostRequestsShutdown() => myRecordingHostLease!.RequestShutdown();

    [When("the controllable host fails its server")]
    public void WhenTheControllableHostFailsItsServer()
    {
        var failure = new InvalidOperationException("recording host server failed");
        if (myFaultingHostLease is not null)
        {
            myFaultingHostLease.FailServer(failure);
        }
        else
        {
            myRecordingHostLease!.FailServer(failure);
        }
    }

    [When("the application lifecycle reaches readiness")]
    public async Task WhenTheApplicationLifecycleReachesReadiness() => await StartApplicationUntilReadyAsync();

    [Then("the application stopped before readiness")]
    public async Task ThenTheApplicationStoppedBeforeReadiness()
    {
        await CompleteApplicationRunAsync();
        Assert.That(myApplicationRunResult, Is.EqualTo(RunResult.ShutdownBeforeReady));
    }

    [Then("the blocked preparation observed cancellation")]
    public void ThenTheBlockedPreparationObservedCancellation() =>
        Assert.That(myPreparationCanceled!.Task.IsCompletedSuccessfully, Is.True);

    [Then("the application stopped after readiness")]
    public async Task ThenTheApplicationStoppedAfterReadiness()
    {
        await CompleteApplicationRunAsync();
        Assert.That(myApplicationRunResult, Is.EqualTo(RunResult.StoppedAfterReady));
    }

    [Then("no startup collaborator ran")]
    public void ThenNoStartupCollaboratorRan() => Assert.That(myRecordingSleep!.Started, Is.False);

    [Then("readiness was not announced")]
    public void ThenReadinessWasNotAnnounced() => Assert.That(myApplicationReadyCount, Is.Zero);

    [Then("readiness was announced once")]
    public void ThenReadinessWasAnnouncedOnce() => Assert.That(myApplicationReadyCount, Is.EqualTo(1));

    [Then("the application lifecycle failed with {string}")]
    public async Task ThenTheApplicationLifecycleFailedWith(string message)
    {
        await CompleteApplicationRunAsync();
        Assert.That(ExceptionMessages(myApplicationLifecycleFailure), Does.Contain(message));
    }

    [Then("the application lifecycle fails after cleanup")]
    public async Task ThenTheApplicationLifecycleFailsAfterCleanup()
    {
        await CompleteApplicationRunAsync();
        Assert.That(myApplicationLifecycleFailure, Is.Not.Null);
    }

    [Then("the open event observer was canceled without stream completion")]
    public void ThenTheOpenEventObserverWasCanceledWithoutStreamCompletion()
    {
        var session = myBackend.Sessions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(session.EventStreamLeftOpen, Is.True);
            Assert.That(session.EventCancellationObserved, Is.True);
        });
    }

    [Then("the stopping command was rejected")]
    public async Task ThenTheStoppingCommandWasRejected()
    {
        await CompleteApplicationRunAsync();
        Assert.That(myStoppingCommand, Is.Not.Null);
        Assert.That(myStoppingCommand!.IsFaulted, Is.True);
        Assert.That(myStoppingCommand.Exception!.GetBaseException().Message, Is.EqualTo("Squad is shutting down"));
    }

    [Then("the accepted command was canceled before its session disposal")]
    public async Task ThenTheAcceptedCommandWasCanceledBeforeItsSessionDisposal()
    {
        await CompleteApplicationRunAsync();
        var session = myBackend.Sessions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(myInFlightApplicationCommand, Is.Not.Null);
            Assert.That(myInFlightApplicationCommand!.IsCanceled, Is.True);
            Assert.That(session.ActiveSendCountAtDispose, Is.Zero);
        });
    }

    [Then("the application lifecycle contains {string} and {string}")]
    public async Task ThenTheApplicationLifecycleContains(string first, string second)
    {
        await CompleteApplicationRunAsync();
        var messages = ExceptionMessages(myApplicationLifecycleFailure);
        Assert.That(messages, Does.Contain(first).And.Contain(second));
    }

    [Then("the application lifecycle contains {string}")]
    public async Task ThenTheApplicationLifecycleContains(string message)
    {
        await CompleteApplicationRunAsync();
        Assert.That(ExceptionMessages(myApplicationLifecycleFailure), Does.Contain(message));
    }

    [Then("all controllable application resources were disposed")]
    public void ThenAllControllableApplicationResourcesWereDisposed()
    {
        Assert.Multiple(() =>
        {
            // The backend runtime is only created once startup reaches session generation; if the
            // application stopped before then, no runtime exists and no sessions were ever owned/disposed.
            Assert.That(myBackend.Disposed, Is.EqualTo(myBackend.RuntimeCreated));
            Assert.That(myRecordingWindow!.DisposeCount, Is.EqualTo(1));
            Assert.That(myRecordingPump!.Disposed, Is.True);
            Assert.That(myRecordingSleep!.Disposed, Is.True);
            Assert.That(myBackend.Sessions.All(session => (session.DisposeCount > 0) == myBackend.RuntimeCreated), Is.True);
        });
        if (myRecordingHostLease is not null)
        {
            Assert.That(myRecordingHostLease.Disposed, Is.True);
        }
        else
        {
            Assert.That(File.Exists(Path.Combine(myApplicationRoot, ".blaxquad", "host.json")), Is.False);
            Assert.That(HostLease.TryAcquireProbe(myApplicationRoot), Is.True);
        }
    }

    [When("the recording backend reports terminal failure {string}")]
    public void WhenTheRecordingBackendReportsTerminalFailure(string message) =>
        myBackend.FailBackend(message);

    [When("the application window closes while recording {string} fails")]
    public void WhenTheApplicationWindowClosesWhileRecordingFails(string role)
    {
        myRecordingWindow!.Close();
        myBackend.Sessions.Single(session => session.Role == role).Fail("failure during shutdown");
    }

    [When("the application window closes")]
    public void WhenTheApplicationWindowCloses() => myRecordingWindow!.Close();

    [When("the controllable handoff pump fails")]
    public void WhenTheControllableHandoffPumpFails() => myRecordingPump!.Fail();

    [When("the in-flight application command begins")]
    public async Task WhenTheInFlightApplicationCommandBegins()
    {
        myInFlightApplicationCommand = myApplication!.ViewModel.SendAsync("coder", "still working");
        var session = myBackend.Sessions.Single();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (session.Sends.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.That(session.Sends, Has.Some.EqualTo("still working"));
    }

    [When("backend cleanup begins")]
    public async Task WhenBackendCleanupBegins() => await myBackend.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));

    [When("backend cleanup remains blocked for six seconds")]
    public async Task WhenBackendCleanupRemainsBlockedForSixSeconds() => await Task.Delay(TimeSpan.FromSeconds(6));

    [Then("the host lease remains held")]
    public void ThenTheHostLeaseRemainsHeld()
    {
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(myApplicationRoot, ".blaxquad", "host.json")), Is.True);
            Assert.That(HostLease.TryAcquireProbe(myApplicationRoot), Is.False);
        });
    }

    [When("backend cleanup is released")]
    public void WhenBackendCleanupIsReleased() => myBackend.ReleaseDispose();

    [When("the application waits for window closure")]
    public async Task WhenTheApplicationWaitsForWindowClosure() => await myApplicationRun!;

    [When("the recording {string} session emits a started event")]
    public void WhenTheRecordingSessionEmitsAStartedEvent(string role) => Emit(role, new AgentStartedEvent(DateTimeOffset.UtcNow));

    [When("the recording {string} session emits an idle event")]
    public void WhenTheRecordingSessionEmitsAnIdleEvent(string role) => Emit(role, new AgentIdleEvent(DateTimeOffset.UtcNow));

    [When("the recording {string} session emits an error {string}")]
    public void WhenTheRecordingSessionEmitsAnError(string role, string message) => Emit(role, new AgentErrorEvent(DateTimeOffset.UtcNow, message));

    [When("the recording {string} session emits assistant delta {string}")]
    public void WhenTheRecordingSessionEmitsAssistantDelta(string role, string content) => Emit(role, new AgentAssistantMessageEvent(DateTimeOffset.UtcNow, content, true));

    [When("the recording {string} session emits reasoning delta {string}")]
    public void WhenTheRecordingSessionEmitsReasoningDelta(string role, string content) => Emit(role, new AgentReasoningEvent(DateTimeOffset.UtcNow, content, true));

    [When("the recording {string} session emits a final assistant message {string}")]
    public void WhenTheRecordingSessionEmitsAFinalAssistantMessage(string role, string content) => Emit(role, new AgentAssistantMessageEvent(DateTimeOffset.UtcNow, content, false));

    [When("the recording {string} session requests permission {string}")]
    public async Task WhenTheRecordingSessionRequestsPermission(string role, string requestId) =>
        await myViewModel.RequestPermissionAsync(new AgentPermissionRequest(DateTimeOffset.UtcNow, requestId, role, "Run command"));

    [When("the recording {string} session requests input {string}")]
    public async Task WhenTheRecordingSessionRequestsInput(string role, string requestId) =>
        await myViewModel.RequestInputAsync(new AgentInputRequest(DateTimeOffset.UtcNow, requestId, role, "What value?"));

    [When("the recording {string} session requests elicitation {string}")]
    public async Task WhenTheRecordingSessionRequestsElicitation(string role, string requestId) =>
        await myViewModel.RequestElicitationAsync(new AgentElicitationRequest(DateTimeOffset.UtcNow, requestId, role, "Choose", "form"));

    [When("the recording {string} session requests URL elicitation {string}")]
    public async Task WhenTheRecordingSessionRequestsUrlElicitation(string role, string requestId) =>
        await myViewModel.RequestElicitationAsync(new AgentElicitationRequest(DateTimeOffset.UtcNow, requestId, role, "Complete sign-in", "url", null, "https://example.test/authorize"));

    [When("the recording {string} session emits tool start {string}")]
    public void WhenTheRecordingSessionEmitsToolStart(string role, string tool) =>
        EmitToolStart(role, CreateToolCallId(role), tool);

    [When("the recording {string} session emits system message {string}")]
    public void WhenTheRecordingSessionEmitsSystemMessage(string role, string message) =>
        Emit(role, new AgentSystemMessageEvent(DateTimeOffset.UtcNow, message));

    private string CreateToolCallId(string role) => $"{role}-tool-{++myNextToolCallId}";

    private void EmitToolStart(string role, string toolCallId, string tool)
    {
        myActiveToolCallIds[role] = toolCallId;
        myToolOutputNormalizer.Start(toolCallId);
        Emit(role, new AgentToolStartedEvent(DateTimeOffset.UtcNow, toolCallId, tool, null));
    }

    [When("a prompt {string} is sent to {string}")]
    public async Task WhenAPromptIsSentTo(string prompt, string role) => await myViewModel.SendAsync(role, prompt);

    [When("a slow prompt is sent to {string} while a prompt is sent to {string}")]
    public async Task WhenASlowPromptIsSentWhileAnotherPromptIsSent(string slowRole, string otherRole)
    {
        myBackend.Sessions.Single(session => session.Role == slowRole).SendDelay = TimeSpan.FromMilliseconds(100);
        var slow = myViewModel.SendAsync(slowRole, "slow");
        var other = myViewModel.SendAsync(otherRole, "fast");
        await Task.WhenAll(slow, other);
    }

    [Then("ViewModel role {string} has status {string}")]
    public void ThenViewModelRoleHasStatus(string role, string status) => Assert.That(myViewModel.Roles[role].Status, Is.EqualTo(status));

    [Then("the UI snapshot contains the running {string} role with active tool {string}")]
    public void ThenTheUiSnapshotContainsTheRunningRoleWithActiveTool(string role, string tool)
    {
        var snapshot = myViewModel.CreateSnapshot();
        var roleSnapshot = snapshot.GetProperty("roles").EnumerateArray().Single(entry => entry.GetProperty("role").GetString() == role);
        Assert.Multiple(() =>
        {
            Assert.That(roleSnapshot.GetProperty("status").GetString(), Is.EqualTo("running"));
            Assert.That(roleSnapshot.GetProperty("activeTool").GetString(), Is.EqualTo(tool));
        });
    }

    [When("the ViewModel creates snapshots while recording {string} emits {int} assistant updates")]
    public async Task WhenTheViewModelCreatesSnapshotsWhileRecordingSessionEmitsAssistantUpdates(string role, int count)
    {
        var publishing = Task.Run(async () =>
        {
            for (var index = 0; index < count; index++)
            {
                await myViewModel.EnqueueEventAsync(role, new AgentAssistantMessageEvent(DateTimeOffset.UtcNow, $"update {index}", false));
            }
        });

        while (!publishing.IsCompleted)
        {
            myViewModel.CreateSnapshot();
            await Task.Yield();
        }

        await publishing;
        myWorkspace.WaitUntil(() => myViewModel.Roles[role].EventCount == count, "streamed assistant updates");
    }

    [Then("the UI snapshot contains event count {int} for {string}")]
    public void ThenTheUiSnapshotContainsEventCount(int count, string role)
    {
        var snapshot = myViewModel.CreateSnapshot();
        var roleSnapshot = snapshot.GetProperty("roles").EnumerateArray().Single(entry => entry.GetProperty("role").GetString() == role);
        Assert.That(roleSnapshot.GetProperty("eventCount").GetInt32(), Is.EqualTo(count));
    }

    [Then("the UI snapshot contains an {string} transcript entry {string} for {string}")]
    public void ThenTheUiSnapshotContainsTranscriptEntry(string source, string content, string role)
    {
        var roleSnapshot = myViewModel.CreateTranscriptSnapshot(500).Single(entry => entry.Role == role);
        Assert.That(
            roleSnapshot.Entries.Any(entry => entry.Entry.Source == source && entry.Entry.Content == content),
            Is.True);
    }

    [Then("the UI snapshot contains pending permission {string} for {string}")]
    public void ThenTheUiSnapshotContainsPendingPermissionFor(string requestId, string role) =>
        AssertUiSnapshotInteraction("permissions", requestId, role);

    [Then("the UI snapshot contains pending input {string} for {string}")]
    public void ThenTheUiSnapshotContainsPendingInputFor(string requestId, string role) =>
        AssertUiSnapshotInteraction("inputs", requestId, role);

    [Then("the UI snapshot contains pending elicitation {string} for {string}")]
    public void ThenTheUiSnapshotContainsPendingElicitationFor(string requestId, string role) =>
        AssertUiSnapshotInteraction("elicitations", requestId, role);

    [Then("the application ViewModel role {string} has status {string}")]
    public void ThenTheApplicationViewModelRoleHasStatus(string role, string status) => Assert.That(myApplication!.ViewModel.Roles[role].Status, Is.EqualTo(status));

    [Then("the application ViewModel role {string} has error {string}")]
    public void ThenTheApplicationViewModelRoleHasError(string role, string message)
    {
        myWorkspace.WaitUntil(() => myApplication!.ViewModel.Roles[role].Error == message, "SDK-shaped session error");
        Assert.That(myApplication!.ViewModel.Roles[role].Status, Is.EqualTo("error"));
    }

    [Then("the recording application sessions are drained")]
    public void ThenTheRecordingApplicationSessionsAreDrained() => Assert.That(myApplication!.Sessions, Is.Empty);

    [Then("the lifecycle trace shows generation and process-wide cleanup completed despite the cleanup failure")]
    public void ThenTheLifecycleTraceShowsCleanupCompletedDespiteFailure()
    {
        Assert.Multiple(() =>
        {
            // Every session created for the partial start is disposed, including the unpublished "reviewer"
            // session the backend never handed to SquadApplication.
            Assert.That(myBackend.Sessions, Is.All.Matches<RecordingAgentSession>(session => session.Disposed));
            Assert.That(myBackend.Disposed, Is.True);
            Assert.That(myRecordingWindow!.StopCount, Is.EqualTo(1));
            Assert.That(myRecordingWindow.DisposeCount, Is.EqualTo(1));
            Assert.That(myRecordingPump!.Disposed, Is.True);
            Assert.That(myRecordingSleep!.Disposed, Is.True);
        });
        myLifecycleTrace!.AssertOrdered("session.coder.completionResolved", "backend.disposed");
        myLifecycleTrace.AssertOrdered("backend.disposed", "window.stopped");
    }

    [Then("ViewModel role {string} has no error")]
    public void ThenViewModelRoleHasNoError(string role) => Assert.That(myViewModel.Roles[role].Error, Is.Null);

    [Then("ViewModel role {string} is working")]
    public void ThenViewModelRoleIsWorking(string role) => Assert.That(myViewModel.Roles[role].IsWorking, Is.True);

    [Then("ViewModel role {string} is not working")]
    public void ThenViewModelRoleIsNotWorking(string role) => Assert.That(myViewModel.Roles[role].IsWorking, Is.False);

    [Then("ViewModel role {string} is ready for a prompt")]
    public void ThenViewModelRoleIsReadyForAPrompt(string role) => Assert.That(myViewModel!.GetRoleReadiness(role), Is.True);

    [Then("ViewModel role {string} transcript has no entry {string}")]
    public void ThenViewModelRoleTranscriptHasNoEntry(string role, string content) =>
        Assert.That(myViewModel.Roles[role].TranscriptEntries.Any(entry => entry.Content == content), Is.False);

    private void Emit(string role, AgentEvent agentEvent)
    {
        var session = myBackend.Sessions.Single(item => item.Role == role);
        session.Emit(agentEvent);
        (myApplication?.ViewModel ?? myViewModel).EnqueueEventAsync(role, agentEvent).GetAwaiter().GetResult();
    }

    private Ctx BuildApplicationContext() => new()
    {
        WorkingDir = myApplicationRoot,
        ScriptDir = AppContext.BaseDirectory.TrimEnd('/', '\\'),
        ContinueLaunch = true,
        Roles = [new RoleConfigRow("coder", "Coder", "master", myApplicationRoot, "task")],
        StateDir = Path.Combine(myApplicationRoot, ".blaxquad"),
        WorktreesDir = Path.Combine(myApplicationRoot, ".worktrees"),
        HandoffLog = Path.Combine(myApplicationRoot, ".blaxquad", "handoff-delivery.log"),
        RolesDir = Path.Combine(myApplicationRoot, "blaxquad", "roles"),
        ConstitutionFile = Path.Combine(myApplicationRoot, "blaxquad", "constitution.prompt"),
    };

    private static AgentBackendContext BuildAgentBackendContext(Ctx context) =>
        new(
            context.WorkingDir,
            context.ScriptDir,
            context.Roles.Select(role => new AgentRoleContext(
                role.Role,
                role.DisplayName,
                role.WorktreePath,
                "Follow the test instructions.\n",
                role.Permissions,
                role.Model,
                role.Effort)).ToArray(),
            new Dictionary<string, string>());



    private void AttachHostLease()
    {
        myApplicationLease = HostLease.Acquire(myApplicationRoot);
        myApplication = new SquadApplication(SquadStartupPlanFactory.ForWorkspace(myApplicationContext!, new WorkspacePreparer(_ => { })), new RecordingAgentProviderFactory(myBackend), myRecordingPump!, myRecordingWindow!, myRecordingSleep!, viewModel: myApplication!.ViewModel, hostLease: myApplicationLease);
    }

    private void ConfigureControllableApplication(bool blockStartup = false, bool useRealLease = false, bool faultServer = false)
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        myRecordingPump = new RecordingHandoffPump();
        myRecordingSleep = new RecordingSleepInhibitor { BlockStart = blockStartup };
        myRecordingHostLease = useRealLease ? null : new RecordingHostLease();
        myFaultingHostLease = null;
        if (useRealLease)
        {
            myApplicationLease = HostLease.Acquire(myApplicationRoot);
        }
        if (faultServer)
        {
            myApplicationLease = HostLease.Acquire(myApplicationRoot);
            myRecordingHostLease = null;
            myFaultingHostLease = new FaultingHostLease(myApplicationLease);
        }
        myApplication = new SquadApplication(
            SquadStartupPlanFactory.ForWorkspace(myApplicationContext!, new WorkspacePreparer(_ => { })),
            new RecordingAgentProviderFactory(myBackend),
            myRecordingPump,
            myRecordingWindow!,
            myRecordingSleep,
            viewModel: myApplication!.ViewModel,
            hostLease: (IHostLease?)myRecordingHostLease ?? (IHostLease?)myFaultingHostLease ?? myApplicationLease);
    }

    private void AssertUiSnapshotInteraction(string collection, string requestId, string role)
    {
        var snapshot = myViewModel.CreateSnapshot();
        var interaction = snapshot.GetProperty(collection).EnumerateArray().Single(entry => entry.GetProperty("requestId").GetString() == requestId);
        Assert.That(interaction.GetProperty("role").GetString(), Is.EqualTo(role));
    }

    private void StartApplicationRun() => myApplicationRun = myApplication!.RunAsync(AnnounceReadinessAsync, default);

    private async Task BeginExternalShutdownAsync()
    {
        myExternalShutdown = HostControlClient.ShutdownAsync(myApplicationRoot, TimeSpan.FromSeconds(5));
        await myApplicationLease!.ShutdownRequested.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private async Task StartApplicationUntilReadyAsync()
    {
        StartApplicationRun();
        while (myApplicationReadyCount == 0 && !myApplicationRun!.IsCompleted)
        {
            await Task.Delay(10);
        }
        if (myApplicationReadyCount == 0)
        {
            await CompleteApplicationRunAsync();
        }
        Assert.That(myApplicationReadyCount, Is.EqualTo(1));
    }

    private Task AnnounceReadinessAsync()
    {
        myLifecycleTrace?.Record("application.ready");
        myApplicationReadyCount++;
        return Task.CompletedTask;
    }

    private async Task RunApplicationToCompletionAsync()
    {
        StartApplicationRun();
        await CompleteApplicationRunAsync();
    }

    private async Task CompleteApplicationRunAsync()
    {
        if (myApplicationRun is null || myApplicationRunResult is not null || myApplicationLifecycleFailure is not null)
        {
            return;
        }
        try
        {
            myApplicationRunResult = await myApplicationRun;
        }
        catch (Exception exception)
        {
            myApplicationLifecycleFailure = exception;
        }
    }

    private static string ExceptionMessages(Exception? exception) => exception switch
    {
        null => string.Empty,
        AggregateException aggregate => string.Join("\n", aggregate.Flatten().InnerExceptions.Select(ExceptionMessages)),
        _ => exception.Message,
    };
}
