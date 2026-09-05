using global::squad.Specs.Support;
using global::squad.AgentProvider.Abstractions;
using global::squad.AgentProvider.Abstractions.Agents;
using global::squad.Process;
using global::squad.Configuration;
using global::squad.CopilotSdk;
using global::squad.Application;
using global::squad.Handoffs.Delivery;
using global::squad.Transcripts;
using global::squad.Photino;
using global::squad.Ui.Abstractions;
using global::squadHQ.Commands;
using System.Collections.Concurrent;
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
    private bool myFastCompletedBeforeSlow;
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
    private CancellationTokenSource? myApplicationCancellation;
    private Task? myStoppingCommand;
    private Task? myInFlightApplicationCommand;
    private Task? myExternalShutdown;
    private TaskCompletionSource? myPreparationEntered;
    private TaskCompletionSource? myPreparationGate;
    private TaskCompletionSource? myPreparationCanceled;
    private RunResult? myApplicationRunResult;
    private Exception? myApplicationLifecycleFailure;
    private InProcessHandoffPoller? myInProcessHandoffPump;
    private readonly ConcurrentQueue<string> myInProcessHandoffLog = [];
    private bool myRecipientWasUnnotifiedBeforePolling;
    private readonly List<(string Path, string Content)> myRecoveredInboxFiles = [];
    private readonly List<string> mySdkInstructionsSentAfterRegistration = [];
    private Exception? myInteractionCompletionFailure;
    private Task? myAbortTask;
    private Exception? myAbortFailure;
    private Task? myPendingPrompt;
    private Task? myAgentReadinessWait;
    private readonly List<TranscriptUpdate> myTranscriptUpdates = [];
    private readonly CopilotToolOutputNormalizer myToolOutputNormalizer = new();
    private readonly Dictionary<string, string> myActiveToolCallIds = new(StringComparer.Ordinal);
    private int myNextToolCallId;
    private string? myTranscriptHistoryDirectory;
    private Task? myPausedTranscriptPublication;
    private Task<IReadOnlyList<RoleTranscriptSnapshot>>? myBlockedTranscriptSnapshot;
    private TaskCompletionSource? myTranscriptPublicationRelease;
    private TranscriptAnnouncementJournal? myPausedTranscriptJournal;
    private long myInitialTranscriptHighWaterMark;
    private string? myArchivedReconstructionContent;
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

    [Given("a ViewModel retaining {int} entries and {int} content characters")]
    public void GivenAViewModelRetainingEntriesAndContentCharacters(
        int maxEntries,
        int maxContentCharacters)
    {
        myViewModel = new SquadViewModel(new TranscriptRetentionOptions(
            MaxRetainedEntries: maxEntries,
            MaxRetainedContentCharacters: maxContentCharacters,
            MaxRetainedEntryCharacters: maxContentCharacters,
            MaxArchivedEntries: 100,
            MaxArchivedContentCharacters: 10_000,
            MaxArchivedEntryCharacters: 1_000));
        myTranscriptHistoryDirectory = myViewModel.TranscriptHistoryDirectory;
        myBackend = new RecordingAgentBackend();
        myViewModel.InitializeRoles(["coder"]);
        myBackend.AddRole("coder");
        myViewModel.RegisterSession(myBackend.Sessions.Single());
    }

    [Given("a ViewModel retaining {int} entries and archiving {int} entries")]
    public void GivenAViewModelRetainingAndArchivingEntries(
        int maxRetainedEntries,
        int maxArchivedEntries)
    {
        myViewModel = new SquadViewModel(new TranscriptRetentionOptions(
            MaxRetainedEntries: maxRetainedEntries,
            MaxRetainedContentCharacters: 1_000,
            MaxRetainedEntryCharacters: 500,
            MaxArchivedEntries: maxArchivedEntries,
            MaxArchivedContentCharacters: 10_000,
            MaxArchivedEntryCharacters: 1_000));
        myTranscriptHistoryDirectory = myViewModel.TranscriptHistoryDirectory;
        myBackend = new RecordingAgentBackend();
        myViewModel.InitializeRoles(["coder"]);
        myBackend.AddRole("coder");
        myViewModel.RegisterSession(myBackend.Sessions.Single());
    }

    [Given("a ViewModel archiving {int} characters per entry")]
    public void GivenAViewModelArchivingCharactersPerEntry(int maxArchivedEntryCharacters)
    {
        myViewModel = new SquadViewModel(new TranscriptRetentionOptions(
            MaxRetainedEntries: 2,
            MaxRetainedContentCharacters: 1_000,
            MaxRetainedEntryCharacters: 50,
            MaxArchivedEntries: 10,
            MaxArchivedContentCharacters: 10_000,
            MaxArchivedEntryCharacters: maxArchivedEntryCharacters));
        myTranscriptHistoryDirectory = myViewModel.TranscriptHistoryDirectory;
        myBackend = new RecordingAgentBackend();
        myViewModel.InitializeRoles(["coder"]);
        myBackend.AddRole("coder");
        myViewModel.RegisterSession(myBackend.Sessions.Single());
    }

    [Given("a ViewModel retaining {int} characters per entry and archiving {int} characters per entry")]
    public void GivenAViewModelRetainingAndArchivingCharactersPerEntry(
        int maxRetainedEntryCharacters,
        int maxArchivedEntryCharacters)
    {
        myViewModel = new SquadViewModel(new TranscriptRetentionOptions(
            MaxRetainedEntries: 10,
            MaxRetainedContentCharacters: 10_000,
            MaxRetainedEntryCharacters: maxRetainedEntryCharacters,
            MaxArchivedEntries: 10,
            MaxArchivedContentCharacters: 10_000,
            MaxArchivedEntryCharacters: maxArchivedEntryCharacters));
        myTranscriptHistoryDirectory = myViewModel.TranscriptHistoryDirectory;
        myBackend = new RecordingAgentBackend();
        myViewModel.InitializeRoles(["coder"]);
        myBackend.AddRole("coder");
        myViewModel.RegisterSession(myBackend.Sessions.Single());
    }

    [Given("a ViewModel retaining {int} entries and {int} content characters while archiving {int} entries")]
    public void GivenAViewModelRetainingContentWhileArchivingEntries(
        int maxRetainedEntries,
        int maxRetainedContentCharacters,
        int maxArchivedEntries)
    {
        myViewModel = new SquadViewModel(new TranscriptRetentionOptions(
            MaxRetainedEntries: maxRetainedEntries,
            MaxRetainedContentCharacters: maxRetainedContentCharacters,
            MaxRetainedEntryCharacters: maxRetainedContentCharacters / 2,
            MaxArchivedEntries: maxArchivedEntries,
            MaxArchivedContentCharacters: 10_000,
            MaxArchivedEntryCharacters: 1_000));
        myTranscriptHistoryDirectory = myViewModel.TranscriptHistoryDirectory;
        myBackend = new RecordingAgentBackend();
        myViewModel.InitializeRoles(["coder"]);
        myBackend.AddRole("coder");
        myViewModel.RegisterSession(myBackend.Sessions.Single());
    }

    [Given("a SquadApplication with recording roles {string}")]
    public void GivenASquadApplicationWithRecordingRoles(string roles)
    {
        var roleNames = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myApplicationRoot = Path.Combine(Path.GetTempPath(), "blaxquad-viewmodel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(myApplicationRoot, "blaxquad", "roles"));
        File.WriteAllText(Path.Combine(myApplicationRoot, "blaxquad", "constitution.prompt"), "Follow the constitution.\n");
        foreach (var role in roleNames)
            File.WriteAllText(Path.Combine(myApplicationRoot, "blaxquad", "roles", role + ".prompt"), "Follow the role.\n");

        myBackend = new RecordingAgentBackend();
        foreach (var role in roleNames)
            myBackend.AddRole(role);

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
        myApplication = new SquadApplication(ctx, new WorkspacePreparer(_ => { }), myBackend, myRecordingPump, myRecordingWindow, myRecordingSleep, viewModel: viewModel);
    }

    [Given("a SquadApplication with a session that emits while shutting down")]
    public void GivenASquadApplicationWithASessionThatEmitsWhileShuttingDown()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        var session = myBackend.Sessions.Single();
        session.IgnoreEventCancellation = true;
        session.OnDispose = () => session.Emit(new AgentStartedEvent(DateTimeOffset.UtcNow));
    }

    [Given("a SquadApplication with recording roles {string} and a lifecycle trace")]
    public void GivenASquadApplicationWithRecordingRolesAndALifecycleTrace(string roles)
    {
        GivenASquadApplicationWithRecordingRoles(roles);
        WireLifecycleTrace();
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
            session.Trace = myLifecycleTrace;
    }

    [Given("a SquadApplication constructed with empty roles and a startup lifecycle trace")]
    public void GivenASquadApplicationConstructedWithEmptyRolesAndAStartupLifecycleTrace()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        var context = myApplicationContext!;
        var preparedRoles = context.Roles.ToList();
        context.Roles = [];
        WireLifecycleTrace();

        var viewModel = myApplication!.ViewModel;
        var roleInitializationRecorded = false;
        viewModel.StateChanged += () =>
        {
            if (roleInitializationRecorded || !viewModel.Roles.ContainsKey("coder"))
                return;

            Assert.That(
                Directory.Exists(context.StateDir),
                Is.False,
                "Role initialization must precede workspace preparation.");
            roleInitializationRecorded = true;
            myLifecycleTrace!.Record("roles.initialized");
        };

        myRecordingHostLease = new RecordingHostLease { Trace = myLifecycleTrace };
        myRecordingWindow!.OnStart = () =>
        {
            Assert.That(Directory.Exists(context.StateDir), Is.True);
            Assert.That(
                Directory.Exists(Path.Combine(
                    context.WorkingDir,
                    ".blaxquad",
                    "handoffs",
                    "inbox",
                    "new")),
                Is.True);
            myLifecycleTrace!.Record("workspace.prepared");
        };

        myApplication = new SquadApplication(
            context,
            new WorkspacePreparer(_ => { }),
            myBackend,
            myRecordingPump!,
            myRecordingWindow,
            myRecordingSleep!,
            viewModel: viewModel,
            hostLease: myRecordingHostLease,
            postLockPreparation: cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                myLifecycleTrace!.Record("postLockPreparation.started");
                context.Roles = preparedRoles;
                myLifecycleTrace.Record("roles.populated");
                myLifecycleTrace.Record("backend.prepared");
                myLifecycleTrace.Record("postLockPreparation.completed");
                return Task.CompletedTask;
            });
    }

    [Given("a SquadApplication with recording roles and a host lease")]
    public void GivenASquadApplicationWithRecordingRolesAndAHostLease()
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        myApplicationLease = HostLease.Acquire(myApplicationRoot);
        myApplication = new SquadApplication(myApplicationContext!, new WorkspacePreparer(_ => { }), myBackend, myRecordingPump!, myRecordingWindow!, myRecordingSleep!, viewModel: myApplication!.ViewModel, hostLease: myApplicationLease);
    }

    [When("the leased SquadApplication starts")]
    public async Task WhenTheLeasedSquadApplicationStarts() => await StartApplicationUntilReadyAsync();

    [Then("the leased SquadApplication start completes")]
    public void ThenTheLeasedSquadApplicationStartCompletes() => Assert.That(myApplication!.Sessions, Has.Count.EqualTo(1));

    [When("the leased SquadApplication stops")]
    public async Task WhenTheLeasedSquadApplicationStops()
    {
        myRecordingWindow!.Close();
        await myApplicationRun!;
    }

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
            myApplicationContext!,
            new WorkspacePreparer(_ => { }),
            myBackend,
            myRecordingPump!,
            myRecordingWindow!,
            myRecordingSleep!,
            viewModel: myApplication!.ViewModel,
            hostLease: myApplicationLease,
            postLockPreparation: async cancellationToken =>
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
            });
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
            myApplicationContext!,
            new WorkspacePreparer(_ => { }),
            myBackend,
            myRecordingPump!,
            myRecordingWindow!,
            myRecordingSleep!,
            viewModel: myApplication!.ViewModel,
            hostLease: myApplicationLease,
            postLockPreparation: _ => throw new CliExitException(1, "recording CLI startup failure"));
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
                    mySdkInstructionsSentAfterRegistration.Add(role.Role);
            };
        }

        var viewModel = myApplication!.ViewModel;
        myApplication = new SquadApplication(
            myApplicationContext,
            new WorkspacePreparer(_ => { }),
            myBackend,
            myRecordingPump!,
            myRecordingWindow!,
            myRecordingSleep!,
            viewModel: viewModel);
    }

    [Given("the SDK-shaped backend fails after its first session")]
    public void GivenTheSdkShapedBackendFailsAfterItsFirstSession() => myBackend.FailAfterCreatingSessionCount = 1;

    [Given("a SquadApplication with an in-process handoff poller and a pending handoff")]
    public void GivenASquadApplicationWithAnInProcessHandoffPollerAndAPendingHandoff()
    {
        ConfigureInProcessHandoffApplication();
        var recipient = myBackend.Sessions.Single(session => session.Role == "reviewer");
        recipient.SendDelay = TimeSpan.FromMilliseconds(100);
        myRecordingWindow!.OnSessionsStarted = () =>
        {
            myRecipientWasUnnotifiedBeforePolling = recipient.Sends.IsEmpty;
            myInFlightApplicationCommand = myApplication!.ViewModel.SendAsync("reviewer", "busy");
        };
        WritePendingHandoff();
    }

    [Given("a SquadApplication with an in-process handoff poller and a {word} recipient")]
    public void GivenASquadApplicationWithAnInProcessHandoffPollerAndARecipient(string state)
    {
        ConfigureInProcessHandoffApplication(state);
    }

    [Given("the in-process poller has a pending handoff")]
    public void GivenTheInProcessPollerHasAPendingHandoff() => WritePendingHandoff();

    [Given("a SquadApplication with an in-process handoff poller and recovered inbox work")]
    public void GivenASquadApplicationWithAnInProcessHandoffPollerAndRecoveredInboxWork()
    {
        ConfigureInProcessHandoffApplication();
        myRecoveredInboxFiles.Clear();
        WriteRecoveredInboxWork("new", "recovery-new.handoff");
        WriteRecoveredInboxWork("in_process", "recovery-in-process.handoff");
    }

    [Given("a cancellable SquadApplication with an in-process handoff poller")]
    public void GivenACancellableSquadApplicationWithAnInProcessHandoffPoller()
    {
        ConfigureInProcessHandoffApplication();
        myApplicationCancellation = new CancellationTokenSource();
    }

    [Given("a leased application blocked before registering its {string} session")]
    public async Task GivenALeasedApplicationBlockedBeforeRegisteringItsSession(string role)
    {
        GivenASquadApplicationWithRecordingRoles("coder,reviewer");
        myBackend.BlockBeforeSessionIndex = myBackend.Sessions
            .Select((session, index) => (session, index))
            .Single(item => item.session.Role == role)
            .index;
        myBackend.Sessions.Single(session => session.Role == role)
            .Emit(new AgentIdleEvent(DateTimeOffset.UtcNow));
        AttachHostLease();
        StartApplicationRun();
        await myBackend.RegistrationBlocked.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [When("the pending session registration completes")]
    public async Task WhenThePendingSessionRegistrationCompletes()
    {
        myBackend.ReleaseRegistration();
        while (myApplicationReadyCount == 0 && !myApplicationRun!.IsCompleted)
            await Task.Delay(10);
        Assert.That(myApplicationReadyCount, Is.EqualTo(1));
    }

    [When("the host client begins waiting for the {string} agent")]
    public void WhenTheHostClientBeginsWaitingForTheAgent(string role) =>
        myAgentReadinessWait = HostControlClient.WaitForAgentAsync(
            myApplicationRoot,
            role,
            TimeSpan.FromSeconds(5));

    [Then("the host client remains waiting for agent readiness")]
    public async Task ThenTheHostClientRemainsWaitingForAgentReadiness()
    {
        await Task.Delay(200);
        Assert.That(myAgentReadinessWait!.IsCompleted, Is.False);
    }

    [Then("the host client readiness wait succeeds")]
    public async Task ThenTheHostClientReadinessWaitSucceeds() =>
        await myAgentReadinessWait!.WaitAsync(TimeSpan.FromSeconds(2));

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

    [Given("a controllable SquadApplication that cancels its caller when ready")]
    public void GivenAControllableSquadApplicationThatCancelsItsCallerWhenReady()
    {
        ConfigureControllableApplication(useRealLease: true);
        myApplicationCancellation = new CancellationTokenSource();
        myRecordingWindow!.OnSessionsStarted = myApplicationCancellation.Cancel;
    }

    [Given("a controllable SquadApplication with a failing window close")]
    public void GivenAControllableSquadApplicationWithAFailingWindowClose()
    {
        ConfigureControllableApplication(useRealLease: true);
        myRecordingWindow!.FailOnClose = true;
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
            myFaultingHostLease.FailServer(failure);
        else
            myRecordingHostLease!.FailServer(failure);
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

    [Then("the application lifecycle was canceled")]
    public async Task ThenTheApplicationLifecycleWasCanceled()
    {
        await CompleteApplicationRunAsync();
        Assert.That(myApplicationLifecycleFailure, Is.TypeOf<OperationCanceledException>());
    }

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
            Assert.That(myRecordingHostLease.Disposed, Is.True);
        else
        {
            Assert.That(File.Exists(Path.Combine(myApplicationRoot, ".blaxquad", "host.json")), Is.False);
            Assert.That(HostLease.TryAcquireProbe(myApplicationRoot), Is.True);
        }
    }

    [When("the application recording {string} session emits a started event")]
    public async Task WhenTheApplicationRecordingSessionEmitsAStartedEvent(string role)
    {
        myBackend.Sessions.Single(session => session.Role == role).Emit(new AgentStartedEvent(DateTimeOffset.UtcNow));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (myApplication!.ViewModel.Roles[role].EventCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    [When("the application recording {string} session requests permission {string}")]
    public async Task WhenTheApplicationRecordingSessionRequestsPermission(string role, string requestId)
    {
        myBackend.Sessions.Single(session => session.Role == role).Emit(
            new AgentPermissionRequest(
                DateTimeOffset.UtcNow,
                requestId,
                role,
                "Run command."));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!myApplication!.ViewModel.PendingPermissions.Any(request =>
                   request.Role == role && request.RequestId == requestId) &&
               DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    [When("the application recording {string} session fails with {string}")]
    public async Task WhenTheApplicationRecordingSessionFailsWith(string role, string message)
    {
        myBackend.Sessions.Single(session => session.Role == role).Fail(message);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (myApplication!.ViewModel.Roles[role].Error != message && DateTime.UtcNow < deadline)
            await Task.Delay(10);
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

    [When("a late started event is submitted for application role {string}")]
    public Task WhenALateStartedEventIsSubmittedForApplicationRole(string role) =>
        myApplication!.ViewModel.EnqueueEventAsync(
            role,
            new AgentStartedEvent(DateTimeOffset.UtcNow));

    [When("prompt {string} is sent to application role {string}")]
    public Task WhenPromptIsSentToApplicationRole(string prompt, string role) =>
        myApplication!.ViewModel.SendAsync(role, prompt);

    [When("the SquadApplication stops")]
    public async Task WhenTheSquadApplicationStops()
    {
        myRecordingWindow!.Close();
        await myApplicationRun!;
        if (Directory.Exists(myApplicationRoot))
            Directory.Delete(myApplicationRoot, recursive: true);
    }

    [When("the application window closes")]
    public void WhenTheApplicationWindowCloses() => myRecordingWindow!.Close();

    [When("in-process polling is canceled")]
    public void WhenInProcessPollingIsCanceled() => myApplicationCancellation!.Cancel();

    [When("the controllable handoff pump fails")]
    public void WhenTheControllableHandoffPumpFails() => myRecordingPump!.Fail();

    [When("the in-flight application command begins")]
    public async Task WhenTheInFlightApplicationCommandBegins()
    {
        myInFlightApplicationCommand = myApplication!.ViewModel.SendAsync("coder", "still working");
        var session = myBackend.Sessions.Single();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (session.Sends.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(10);
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

    [When("the recording {string} session emits a user message {string}")]
    public void WhenTheRecordingSessionEmitsAUserMessage(string role, string content) => Emit(role, new AgentUserMessageEvent(DateTimeOffset.UtcNow, content));

    [When("the recording {string} session emits a {int} character user message")]
    public void WhenTheRecordingSessionEmitsACharacterUserMessage(
        string role,
        int characterCount) =>
        Emit(role, new AgentUserMessageEvent(DateTimeOffset.UtcNow, new string('x', characterCount)));

    [When("the recording {string} session emits a {int} character patterned user message")]
    public void WhenTheRecordingSessionEmitsACharacterPatternedUserMessage(
        string role,
        int characterCount)
    {
        myArchivedReconstructionContent = new string(
            Enumerable.Range(0, characterCount)
                .Select(index => (char)('a' + index % 26))
                .ToArray());
        Emit(
            role,
            new AgentUserMessageEvent(
                DateTimeOffset.UtcNow,
                myArchivedReconstructionContent));
    }

    [When("the recording {string} session emits {int} user messages")]
    public void WhenTheRecordingSessionEmitsUserMessages(string role, int count)
    {
        for (var index = 0; index < count; index++)
            Emit(role, new AgentUserMessageEvent(DateTimeOffset.UtcNow, $"message-{index}"));
    }

    [When("the recording {string} session emits {int} harness messages")]
    public void WhenTheRecordingSessionEmitsHarnessMessages(string role, int count)
    {
        for (var index = 0; index < count; index++)
            Emit(role, new AgentHarnessMessageEvent(DateTimeOffset.UtcNow, $"message-{index}"));
    }

    [When("the recording {string} session emits harness message {string}")]
    public void WhenTheRecordingSessionEmitsHarnessMessage(string role, string content) => Emit(role, new AgentHarnessMessageEvent(DateTimeOffset.UtcNow, content));

    [When("the recording {string} session emits reasoning delta {string}")]
    public void WhenTheRecordingSessionEmitsReasoningDelta(string role, string content) => Emit(role, new AgentReasoningEvent(DateTimeOffset.UtcNow, content, true));

    [When("the recording {string} session emits final reasoning {string}")]
    public void WhenTheRecordingSessionEmitsFinalReasoning(string role, string content) => Emit(role, new AgentReasoningEvent(DateTimeOffset.UtcNow, content, false));

    [When("the recording {string} session emits tool output {string}")]
    public void WhenTheRecordingSessionEmitsToolOutput(string role, string content)
    {
        var toolCallId = myActiveToolCallIds[role];
        var output = myToolOutputNormalizer.Apply(toolCallId, DecodeEscapes(content));
        if (output is not null)
            Emit(role, new AgentToolOutputChangedEvent(DateTimeOffset.UtcNow, toolCallId, output));
    }

    [When("the recording {string} session emits a final assistant message {string}")]
    public void WhenTheRecordingSessionEmitsAFinalAssistantMessage(string role, string content) => Emit(role, new AgentAssistantMessageEvent(DateTimeOffset.UtcNow, content, false));

    [When("the recording {string} session requests permission {string}")]
    public async Task WhenTheRecordingSessionRequestsPermission(string role, string requestId) =>
        await myViewModel.RequestPermissionAsync(new AgentPermissionRequest(DateTimeOffset.UtcNow, requestId, role, "Run command"));

    [When("the recording {string} session requests input {string}")]
    public async Task WhenTheRecordingSessionRequestsInput(string role, string requestId) =>
        await myViewModel.RequestInputAsync(new AgentInputRequest(DateTimeOffset.UtcNow, requestId, role, "What value?"));

    [When("the recording {string} session requests input {string} with choices {string}")]
    public async Task WhenTheRecordingSessionRequestsInputWithChoices(string role, string requestId, string choices) =>
        await myViewModel.RequestInputAsync(new AgentInputRequest(DateTimeOffset.UtcNow, requestId, role, "Choose", choices.Split(','), false));

    [When("the recording {string} session requests elicitation {string}")]
    public async Task WhenTheRecordingSessionRequestsElicitation(string role, string requestId) =>
        await myViewModel.RequestElicitationAsync(new AgentElicitationRequest(DateTimeOffset.UtcNow, requestId, role, "Choose", "form"));

    [When("the recording {string} session requests URL elicitation {string}")]
    public async Task WhenTheRecordingSessionRequestsUrlElicitation(string role, string requestId) =>
        await myViewModel.RequestElicitationAsync(new AgentElicitationRequest(DateTimeOffset.UtcNow, requestId, role, "Complete sign-in", "url", null, "https://example.test/authorize"));

    [When("permission {string} is completed")]
    public async Task WhenPermissionIsCompleted(string requestId) =>
        await CompleteInteractionAsync(() => myViewModel.CompletePermissionAsync(requestId));

    [When("permission {string} is rejected for {string}")]
    public async Task WhenPermissionIsRejectedFor(string requestId, string role) =>
        await CompleteInteractionAsync(() => myViewModel.CompletePermissionAsync(role, requestId, false));

    [When("permission {string} is approved for {string}")]
    public async Task WhenPermissionIsApprovedFor(string requestId, string role) =>
        await CompleteInteractionAsync(() => myViewModel.CompletePermissionAsync(role, requestId, true));

    [When("input {string} is answered {string} for {string}")]
    public async Task WhenInputIsAnsweredFor(string requestId, string answer, string role) =>
        await CompleteInteractionAsync(() => myViewModel.CompleteInputAsync(role, requestId, answer, true));

    [When("elicitation {string} is accepted for {string} with form value {string}")]
    public async Task WhenElicitationIsAcceptedFor(string requestId, string role, string value)
    {
        var content = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["answer"] = value });
        await CompleteInteractionAsync(() => myViewModel.CompleteElicitationAsync(role, requestId, "accept", content));
    }

    [When("the ViewModel stops")]
    public async Task WhenTheViewModelStops() => await myViewModel.StopAsync();

    [When("the recording {string} session emits tool start {string}")]
    public void WhenTheRecordingSessionEmitsToolStart(string role, string tool) =>
        EmitToolStart(role, CreateToolCallId(role), tool);

    [When("the recording {string} session emits tool start {string} with arguments:")]
    public void WhenTheRecordingSessionEmitsToolStartWithArguments(string role, string tool, string arguments) =>
        EmitToolStart(role, CreateToolCallId(role), tool, arguments);

    [When("the recording {string} session emits tool start {string} for {string}")]
    public void WhenTheRecordingSessionEmitsToolStartForPath(string role, string tool, string path) =>
        EmitToolStart(
            role,
            CreateToolCallId(role),
            tool,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = path }));

    [When("SDK tool call {string} starts {string} for role {string}")]
    public void WhenSdkToolCallStartsForRole(string toolCallId, string tool, string role) =>
        EmitToolStart(role, toolCallId, tool);

    [When("SDK tool call {string} emits partial output {string} for role {string}")]
    public void WhenSdkToolCallEmitsPartialOutputForRole(string toolCallId, string partialOutput, string role)
    {
        var output = myToolOutputNormalizer.Apply(toolCallId, DecodeEscapes(partialOutput));
        if (output is not null)
            Emit(role, new AgentToolOutputChangedEvent(DateTimeOffset.UtcNow, toolCallId, output));
    }

    [When("tool call {string} reports progress {string} for role {string}")]
    public void WhenToolCallReportsProgressForRole(string toolCallId, string progress, string role) =>
        Emit(role, new AgentToolProgressEvent(DateTimeOffset.UtcNow, toolCallId, progress));

    [When("SDK tool call {string} completes for role {string} with detailed output {string}")]
    public void WhenSdkToolCallCompletesForRoleWithDetailedOutput(string toolCallId, string role, string detailedOutput)
    {
        var streamedOutput = myToolOutputNormalizer.Complete(toolCallId);
        Emit(role, new AgentToolCompletedEvent(
            DateTimeOffset.UtcNow,
            toolCallId,
            "powershell",
            true,
            streamedOutput ? null : DecodeEscapes(detailedOutput)));
    }

    [When("the recording {string} session emits system message {string}")]
    public void WhenTheRecordingSessionEmitsSystemMessage(string role, string message) =>
        Emit(role, new AgentSystemMessageEvent(DateTimeOffset.UtcNow, message));

    [When("the recording {string} session invokes skill {string}")]
    public void WhenTheRecordingSessionInvokesSkill(string role, string name) =>
        Emit(role, new AgentSkillInvokedEvent(DateTimeOffset.UtcNow, name));

    [When("the recording {string} session starts subagent {string} displayed as {string} using model {string}")]
    public void WhenTheRecordingSessionStartsSubagent(string role, string agentName, string displayName, string model) =>
        Emit(role, new AgentSubagentStartedEvent(
            DateTimeOffset.UtcNow,
            NullIfEmpty(agentName),
            NullIfEmpty(displayName),
            NullIfEmpty(model)));

    [When("the recording {string} session emits tool completion {string}")]
    public void WhenTheRecordingSessionEmitsToolCompletion(string role, string tool)
    {
        var toolCallId = myActiveToolCallIds[role];
        myToolOutputNormalizer.Complete(toolCallId);
        Emit(role, new AgentToolCompletedEvent(DateTimeOffset.UtcNow, toolCallId, tool, true));
    }

    [When("the recording {string} session emits tool completion {string} with output {string}")]
    public void WhenTheRecordingSessionEmitsToolCompletionWithOutput(string role, string tool, string output)
    {
        var toolCallId = myActiveToolCallIds[role];
        var streamedOutput = myToolOutputNormalizer.Complete(toolCallId);
        Emit(role, new AgentToolCompletedEvent(
            DateTimeOffset.UtcNow,
            toolCallId,
            tool,
            true,
            streamedOutput ? null : output));
    }

    [When("the recording {string} session emits tool completion {string} with display output {string} and content {string}")]
    public void WhenTheRecordingSessionEmitsToolCompletionWithDisplayOutputAndContent(string role, string tool, string displayOutput, string content)
    {
        var toolCallId = myActiveToolCallIds[role];
        myToolOutputNormalizer.Complete(toolCallId);
        Emit(role, new AgentToolCompletedEvent(
            DateTimeOffset.UtcNow,
            toolCallId,
            tool,
            true,
            displayOutput,
            DecodeEscapes(content)));
    }

    private string CreateToolCallId(string role) => $"{role}-tool-{++myNextToolCallId}";

    private void EmitToolStart(string role, string toolCallId, string tool, string? arguments = null)
    {
        myActiveToolCallIds[role] = toolCallId;
        myToolOutputNormalizer.Start(toolCallId);
        Emit(role, new AgentToolStartedEvent(DateTimeOffset.UtcNow, toolCallId, tool, arguments));
    }

    private static string DecodeEscapes(string value) =>
        value.Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal);

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    [When("the recording {string} session reports {long} context tokens of {long}")]
    public void WhenTheRecordingSessionReportsContextUsage(string role, long usedTokens, long limitTokens) =>
        Emit(role, new AgentContextUsageEvent(DateTimeOffset.UtcNow, usedTokens, limitTokens));

    [When("the recording {string} session reports {decimal} AIC used")]
    public void WhenTheRecordingSessionReportsAicUsage(string role, decimal aicUsed) =>
        Emit(role, new AgentSessionUsageEvent(DateTimeOffset.UtcNow, aicUsed));

    [When("a prompt {string} is sent to {string}")]
    public async Task WhenAPromptIsSentTo(string prompt, string role) => await myViewModel.SendAsync(role, prompt);

    [When("overlapping prompts {string} are sent to {string}")]
    public async Task WhenOverlappingPromptsAreSentTo(string prompts, string role)
    {
        myBackend.Sessions.Single(session => session.Role == role).SendDelay = TimeSpan.FromMilliseconds(50);
        await Task.WhenAll(prompts.Split(',').Select(prompt => myViewModel.SendAsync(role, prompt)));
    }

    [When("a slow prompt is sent to {string} while a prompt is sent to {string}")]
    public async Task WhenASlowPromptIsSentWhileAnotherPromptIsSent(string slowRole, string otherRole)
    {
        myBackend.Sessions.Single(session => session.Role == slowRole).SendDelay = TimeSpan.FromMilliseconds(100);
        var slow = myViewModel.SendAsync(slowRole, "slow");
        var other = myViewModel.SendAsync(otherRole, "fast");
        await Task.WhenAll(slow, other);
    }

    [When("a slow prompt is sent to {string} while a prompt is sent to {string} concurrently")]
    public async Task WhenASlowPromptIsSentWhileAnotherPromptIsSentConcurrently(string slowRole, string otherRole)
    {
        myBackend.Sessions.Single(session => session.Role == slowRole).SendDelay = TimeSpan.FromMilliseconds(250);
        var slow = myViewModel.SendAsync(slowRole, "slow");
        var fast = myViewModel.SendAsync(otherRole, "fast");
        await fast.WaitAsync(TimeSpan.FromMilliseconds(100));
        myFastCompletedBeforeSlow = !slow.IsCompleted;
        await slow;
    }

    [When("{string} is aborted")]
    public async Task WhenRoleIsAborted(string role) => await myViewModel.AbortAsync(role);

    [When("a slow prompt {string} starts for {string}")]
    public void WhenASlowPromptStarts(string prompt, string role)
    {
        var session = myBackend.Sessions.Single(session => session.Role == role);
        session.SendDelay = TimeSpan.FromSeconds(30);
        myInFlightApplicationCommand = myViewModel.SendAsync(role, prompt);
        myWorkspace.WaitUntil(() => session.Sends.Contains(prompt), "slow prompt start");
    }

    [When("cancellation starts for {string}")]
    public async Task WhenCancellationStarts(string role)
    {
        var session = myBackend.Sessions.Single(session => session.Role == role);
        session.BlockAbort = true;
        myAbortTask = myViewModel.AbortAsync(role);
        await session.AbortEntered.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [When("prompt {string} is started while cancellation is pending for {string}")]
    public void WhenPromptIsStartedWhileCancellationIsPending(string prompt, string role) =>
        myPendingPrompt = myViewModel.SendAsync(role, prompt);

    [Then("the pending prompt has not been sent to {string}")]
    public void ThenThePendingPromptHasNotBeenSent(string role) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).Sends, Has.None.EqualTo("second"));

    [When("cancellation completes for {string}")]
    public async Task WhenCancellationCompletes(string role)
    {
        myBackend.Sessions.Single(session => session.Role == role).ReleaseAbort();
        await Task.WhenAll(myAbortTask!, myPendingPrompt!).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [When("cancellation fails for {string}")]
    public async Task WhenCancellationFails(string role)
    {
        var session = myBackend.Sessions.Single(session => session.Role == role);
        session.FailAbort = true;
        try
        {
            await myViewModel.AbortAsync(role);
        }
        catch (InvalidOperationException exception) when (exception.Message == "recording abort failed")
        {
            myAbortFailure = exception;
        }
        finally
        {
            session.FailAbort = false;
        }
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
                await myViewModel.EnqueueEventAsync(role, new AgentAssistantMessageEvent(DateTimeOffset.UtcNow, $"update {index}", false));
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

    [Then("the UI snapshot contains {long} context tokens of {long} for {string}")]
    public void ThenTheUiSnapshotContainsContextUsage(long usedTokens, long limitTokens, string role)
    {
        var snapshot = myViewModel.CreateSnapshot();
        var roleSnapshot = snapshot.GetProperty("roles").EnumerateArray().Single(entry => entry.GetProperty("role").GetString() == role);
        Assert.Multiple(() =>
        {
            Assert.That(roleSnapshot.GetProperty("contextUsedTokens").GetInt64(), Is.EqualTo(usedTokens));
            Assert.That(roleSnapshot.GetProperty("contextLimitTokens").GetInt64(), Is.EqualTo(limitTokens));
        });
    }

    [Then("the UI snapshot contains {decimal} AIC used for {string}")]
    public void ThenTheUiSnapshotContainsAicUsage(decimal aicUsed, string role)
    {
        var snapshot = myViewModel.CreateSnapshot();
        var roleSnapshot = snapshot.GetProperty("roles").EnumerateArray().Single(entry => entry.GetProperty("role").GetString() == role);
        Assert.That(roleSnapshot.GetProperty("aicUsed").GetDecimal(), Is.EqualTo(aicUsed));
    }

    [Then("the UI snapshot contains an {string} transcript entry {string} for {string}")]
    public void ThenTheUiSnapshotContainsTranscriptEntry(string source, string content, string role)
    {
        var roleSnapshot = myViewModel.CreateTranscriptSnapshot(500).Single(entry => entry.Role == role);
        Assert.That(
            roleSnapshot.Entries.Any(entry => entry.Entry.Source == source && entry.Entry.Content == content),
            Is.True);
    }

    [Then("the UI state snapshot excludes transcript history")]
    public void ThenTheUiStateSnapshotExcludesTranscriptHistory()
    {
        var roles = myViewModel.CreateSnapshot().GetProperty("roles").EnumerateArray();
        Assert.That(roles.All(role => !role.TryGetProperty("transcriptEntries", out _)), Is.True);
    }

    [Then("the transcript updates for {string} are")]
    public void ThenTheTranscriptUpdatesForAre(string role, Table expected)
    {
        var actual = myTranscriptUpdates
            .Where(update => update.Role == role)
            .Select(update => new
            {
                Sequence = update.Sequence.ToString(),
                Operation = update.Kind.ToString(),
                Index = update.EntryIndex.ToString(),
                Content = update.Entry?.Content ?? update.Content ?? "",
            })
            .ToArray();
        Assert.That(actual, Has.Length.EqualTo(expected.RowCount));
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[index].Sequence, Is.EqualTo(expected.Rows[index]["sequence"]));
                Assert.That(actual[index].Operation, Is.EqualTo(expected.Rows[index]["operation"]));
                Assert.That(actual[index].Index, Is.EqualTo(expected.Rows[index]["index"]));
                Assert.That(actual[index].Content, Is.EqualTo(expected.Rows[index]["content"]));
            });
        }
    }

    [Then("the transcript announcements for {string} are")]
    public void ThenTheTranscriptAnnouncementsForAre(string role, Table expected)
    {
        var actual = myTranscriptUpdates
            .Where(update => update.Role == role && update.Announcement is not null)
            .Select(update => new
            {
                Sequence = update.Sequence.ToString(),
                Operation = update.Announcement?.Kind.ToString() ?? "",
                Content = update.Announcement?.Content ?? "",
            })
            .ToArray();
        Assert.That(actual, Has.Length.EqualTo(expected.RowCount));
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[index].Sequence, Is.EqualTo(expected.Rows[index]["sequence"]));
                Assert.That(actual[index].Operation, Is.EqualTo(expected.Rows[index]["operation"]));
                Assert.That(actual[index].Content, Is.EqualTo(expected.Rows[index]["content"]));
            });
        }
    }

    [When("transcript publication for {string} pauses after an assistant delta {string}")]
    public async Task WhenTranscriptPublicationPausesAfterAnAssistantDelta(
        string role,
        string content)
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        myTranscriptPublicationRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        myPausedTranscriptJournal = new TranscriptAnnouncementJournal(100, 10_000);
        myViewModel.TranscriptChanged += update =>
        {
            if (update.Role != role)
                return;
            entered.TrySetResult();
            myTranscriptPublicationRelease.Task.GetAwaiter().GetResult();
            myPausedTranscriptJournal.Add(update);
        };
        myPausedTranscriptPublication = myViewModel.EnqueueEventAsync(
            role,
            new AgentAssistantMessageEvent(
                DateTimeOffset.UtcNow,
                content,
                true));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        myBlockedTranscriptSnapshot = Task.Run(
            () => myViewModel.CreateTranscriptSnapshot(500));
    }

    [Then("transcript snapshot capture waits for the publication and includes announcement {string}")]
    public async Task ThenTranscriptSnapshotCaptureWaitsForPublication(
        string content)
    {
        await Task.Delay(50);
        Assert.That(myBlockedTranscriptSnapshot!.IsCompleted, Is.False);
        myTranscriptPublicationRelease!.TrySetResult();
        await myPausedTranscriptPublication!;
        var snapshot = await myBlockedTranscriptSnapshot;
        var roleSnapshot = snapshot.Single();
        var recovery = myPausedTranscriptJournal!.Read(
            roleSnapshot.Role,
            0,
            roleSnapshot.Sequence);
        Assert.Multiple(() =>
        {
            Assert.That(roleSnapshot.Sequence, Is.EqualTo(1));
            Assert.That(
                recovery.Fragments.Select(
                    fragment => fragment.Announcement.Content),
                Is.EqualTo(new[] { content }));
        });
    }

    [When("the initial transcript high-water mark for {string} is captured")]
    public void WhenTheInitialTranscriptHighWaterMarkIsCaptured(string role) =>
        myInitialTranscriptHighWaterMark = myViewModel
            .CreateTranscriptSnapshot(1)
            .Single(snapshot => snapshot.Role == role)
            .Sequence;

    [Then("initial transcript synchronization for {string} announces {string} exactly once after the high-water mark")]
    public void ThenInitialTranscriptSynchronizationAnnouncesExactlyOnce(
        string role,
        string content)
    {
        var journal = new TranscriptAnnouncementJournal(100, 10_000);
        foreach (var update in myTranscriptUpdates.Where(
                     update => update.Role == role))
            journal.Add(update);
        var snapshot = myViewModel.CreateTranscriptSnapshot(500);
        var roleSnapshot = snapshot.Single(item => item.Role == role);
        var interval = journal.Read(
            role,
            myInitialTranscriptHighWaterMark,
            roleSnapshot.Sequence);
        var payload = JsonSerializer.SerializeToElement(
            PhotinoTranscriptProtocol.CreateSynchronizationPayload(
                snapshot,
                new Dictionary<string, TranscriptRecoveryAnnouncement>(
                    StringComparer.Ordinal)
                {
                    [role] = interval,
                }));
        var synchronizedRole = payload.GetProperty("roles")
            .EnumerateArray()
            .Single(item => item.GetProperty("role").GetString() == role);
        Assert.Multiple(() =>
        {
            Assert.That(
                synchronizedRole.GetProperty("announcementAfter").GetInt64(),
                Is.EqualTo(myInitialTranscriptHighWaterMark));
            Assert.That(
                synchronizedRole.GetProperty("announcementThrough").GetInt64(),
                Is.EqualTo(roleSnapshot.Sequence));
            Assert.That(
                synchronizedRole.GetProperty("announcement")
                    .GetProperty("fragments")
                    .EnumerateArray()
                    .Select(fragment => fragment.GetProperty("content").GetString()),
                Is.EqualTo(new[] { content }));
        });
    }

    [Then("the latest transcript announcement contains {int} characters and reports truncation")]
    public void ThenTheLatestTranscriptAnnouncementIsBounded(int characterCount)
    {
        var announcement = myTranscriptUpdates.Last().Announcement;
        Assert.Multiple(() =>
        {
            Assert.That(announcement, Is.Not.Null);
            Assert.That(announcement!.Content, Has.Length.EqualTo(characterCount));
            Assert.That(announcement.Truncated, Is.True);
        });
    }

    [Then("a {int} update recovery announcement journal for {string} reports truncation after sequence {long}")]
    public void ThenARecoveryAnnouncementJournalReportsTruncation(
        int maximumUpdates,
        string role,
        long sequence)
    {
        var journal = new TranscriptAnnouncementJournal(maximumUpdates, 10_000);
        foreach (var update in myTranscriptUpdates.Where(update => update.Role == role))
            journal.Add(update);

        var recovery = journal.Read(role, sequence, myTranscriptUpdates.Max(update => update.Sequence));
        Assert.Multiple(() =>
        {
            Assert.That(recovery.Truncated, Is.True);
            Assert.That(
                recovery.Fragments.Select(fragment => fragment.Announcement.Content),
                Is.EqualTo(new[] { "world" }));
        });
    }

    [Then("the Photino transcript delta excludes earlier transcript content")]
    public void ThenThePhotinoTranscriptDeltaExcludesEarlierTranscriptContent()
    {
        var update = myTranscriptUpdates.Single(item => item.Kind == TranscriptUpdateKind.AppendContent);
        var payload = JsonSerializer.SerializeToElement(
            PhotinoTranscriptProtocol.CreateUpdatePayload(update));
        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("operation").GetString(), Is.EqualTo("append-content"));
            Assert.That(payload.GetProperty("content").GetString(), Is.EqualTo("world"));
            Assert.That(
                payload.GetProperty("announcement").GetProperty("operation").GetString(),
                Is.EqualTo("append-content"));
            Assert.That(
                payload.GetProperty("announcement").GetProperty("entryIndex").GetInt32(),
                Is.EqualTo(update.EntryIndex));
            Assert.That(
                payload.GetProperty("announcement").GetProperty("content").GetString(),
                Is.EqualTo("world"));
            Assert.That(
                payload.GetProperty("announcement").GetProperty("truncated").GetBoolean(),
                Is.False);
            Assert.That(payload.GetRawText(), Does.Not.Contain("question"));
            Assert.That(payload.GetRawText(), Does.Not.Contain("Hello-"));
        });
    }

    [Then("the Photino recovery synchronization includes announcements for {string} after sequence {long}")]
    public void ThenThePhotinoRecoverySynchronizationIncludesAnnouncements(
        string role,
        long sequence)
    {
        var journal = new TranscriptAnnouncementJournal(100, 10_000);
        foreach (var update in myTranscriptUpdates.Where(update => update.Role == role))
            journal.Add(update);
        var snapshot = myViewModel.CreateTranscriptSnapshot(500);
        var roleSnapshot = snapshot.Single(item => item.Role == role);
        var recovery = new Dictionary<string, TranscriptRecoveryAnnouncement>(
            StringComparer.Ordinal)
        {
            [role] = journal.Read(role, sequence, roleSnapshot.Sequence),
        };
        var payload = JsonSerializer.SerializeToElement(
            PhotinoTranscriptProtocol.CreateSynchronizationPayload(
                snapshot,
                recovery,
                recovery: true));
        var synchronizedRole = payload.GetProperty("roles")
            .EnumerateArray()
            .Single(item => item.GetProperty("role").GetString() == role);
        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("recovery").GetBoolean(), Is.True);
            Assert.That(
                synchronizedRole.GetProperty("announcementAfter").GetInt64(),
                Is.EqualTo(sequence));
            Assert.That(
                synchronizedRole.GetProperty("announcementThrough").GetInt64(),
                Is.EqualTo(roleSnapshot.Sequence));
            Assert.That(
                synchronizedRole.GetProperty("announcement")
                    .GetProperty("fragments")
                    .EnumerateArray()
                    .Select(fragment => fragment.GetProperty("content").GetString()),
                Is.EqualTo(new[] { "Hello-", "world" }));
            Assert.That(
                synchronizedRole.GetProperty("announcement")
                    .GetProperty("fragments")
                    .EnumerateArray()
                    .Select(fragment => fragment.GetProperty("sequence").GetInt64()),
                Is.EqualTo(new long[] { 2, 3 }));
            Assert.That(
                synchronizedRole.GetProperty("announcement")
                    .GetProperty("truncated")
                    .GetBoolean(),
                Is.False);
        });
    }

    [Then("the Photino transcript synchronization preserves the current {string} history")]
    public void ThenThePhotinoTranscriptSynchronizationPreservesCurrentHistory(string role)
    {
        var payload = JsonSerializer.SerializeToElement(
            PhotinoTranscriptProtocol.CreateSynchronizationPayload(
                myViewModel.CreateTranscriptSnapshot(500)));
        var roleSnapshot = payload.GetProperty("roles")
            .EnumerateArray()
            .Single(item => item.GetProperty("role").GetString() == role);
        Assert.Multiple(() =>
        {
            Assert.That(roleSnapshot.GetProperty("sequence").GetInt64(), Is.EqualTo(4));
            Assert.That(
                roleSnapshot.GetProperty("entries")
                    .EnumerateArray()
                    .Select(entry => entry.GetProperty("content").GetString()),
                Is.EqualTo(new[] { "question", "Hello-world" }));
        });
    }

    [Then("a {int} entry transcript synchronization for {string} starts at index {int}")]
    public void ThenATranscriptSynchronizationStartsAtIndex(
        int maxEntries,
        string role,
        int startIndex)
    {
        var snapshot = myViewModel.CreateTranscriptSnapshot(maxEntries)
            .Single(item => item.Role == role);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Entries[0].EntryIndex, Is.EqualTo(startIndex));
            Assert.That(snapshot.Entries, Has.Count.EqualTo(maxEntries));
            Assert.That(snapshot.Sequence, Is.EqualTo(startIndex + maxEntries));
        });
    }

    [Then("the previous transcript page for {string} contains the first {int} entries")]
    public void ThenThePreviousTranscriptPageContainsTheFirstEntries(string role, int entryCount)
    {
        var page = myViewModel.CreateTranscriptPage(role, entryCount, 200);
        Assert.Multiple(() =>
        {
            Assert.That(page.Entries[0].EntryIndex, Is.Zero);
            Assert.That(page.Entries, Has.Count.EqualTo(entryCount));
            Assert.That(page.HasMore, Is.False);
            Assert.That(page.Entries[0].Entry.Content, Is.EqualTo("message-0"));
            Assert.That(page.Entries[^1].Entry.Content, Is.EqualTo($"message-{entryCount - 1}"));
        });
    }

    [Then("ViewModel role {string} retains at most {int} entries and {int} content characters")]
    public void ThenViewModelRoleRetainsAtMostEntriesAndContentCharacters(
        string role,
        int maxEntries,
        int maxContentCharacters)
    {
        var entries = myViewModel.Roles[role].TranscriptEntries;
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.LessThanOrEqualTo(maxEntries));
            Assert.That(entries.Sum(entry => entry.Content.Length), Is.LessThanOrEqualTo(maxContentCharacters));
        });
    }

    [Then("the retained transcript for {string} starts at index {int}")]
    public void ThenTheRetainedTranscriptStartsAtIndex(string role, int entryIndex)
    {
        var snapshot = myViewModel.CreateTranscriptSnapshot(500)
            .Single(item => item.Role == role);
        Assert.That(snapshot.Entries[0].EntryIndex, Is.EqualTo(entryIndex));
        Assert.That(snapshot.HasMore, Is.True);
    }

    [Then("archived transcript history for {string} preserves {string}")]
    public void ThenArchivedTranscriptHistoryPreserves(string role, string content)
    {
        var page = myViewModel.CreateTranscriptPage(role, int.MaxValue, 200);
        Assert.That(page.Entries.Single().Entry.Content, Is.EqualTo(content));
    }

    [Then("the retained transcript entry for {string} offers archived content")]
    public void ThenTheRetainedTranscriptEntryOffersArchivedContent(string role)
    {
        var snapshot = myViewModel.CreateTranscriptSnapshot(500)
            .Single(item => item.Role == role);
        var retainedEntry = snapshot.Entries.Single();
        var archivedEntry = myViewModel.CreateArchivedTranscriptEntry(
            role,
            retainedEntry.EntryIndex);
        Assert.Multiple(() =>
        {
            Assert.That(retainedEntry.HasArchivedContent, Is.True);
            Assert.That(
                archivedEntry.Entry!.Content.Length,
                Is.GreaterThan(retainedEntry.Entry.Content.Length));
            Assert.That(archivedEntry.Sequence, Is.EqualTo(snapshot.Sequence));
        });
    }

    [Then("archived reconstruction inputs for {string} entry {int} are:")]
    public void ThenArchivedReconstructionInputsAre(
        string role,
        int entryIndex,
        Table table)
    {
        var expected = table.Rows.Single();
        var snapshot = myViewModel.CreateTranscriptSnapshot(500)
            .Single(item => item.Role == role);
        var retainedEntry = snapshot.Entries
            .Single(item => item.EntryIndex == entryIndex);
        var archivedEntry = myViewModel.CreateArchivedTranscriptEntry(
            role,
            entryIndex);
        var content = myArchivedReconstructionContent
            ?? throw new InvalidOperationException(
                "Patterned archive content was not emitted.");
        var retainedOffset = long.Parse(expected["retained offset"]);
        var archivedPrefix = int.Parse(expected["archived prefix"]);
        Assert.Multiple(() =>
        {
            Assert.That(
                archivedEntry.Sequence,
                Is.EqualTo(long.Parse(expected["sequence"])));
            Assert.That(
                retainedEntry.ContentStart,
                Is.EqualTo(retainedOffset));
            Assert.That(
                retainedEntry.Entry.Content,
                Has.Length.EqualTo(
                    int.Parse(expected["retained characters"])));
            Assert.That(
                retainedEntry.HasArchivedContent,
                Is.EqualTo(bool.Parse(expected["archive available"])));
            Assert.That(archivedEntry.Entry, Is.Not.Null);
            Assert.That(
                archivedEntry.ContentTruncated,
                Is.EqualTo(bool.Parse(expected["content truncated"])));
            Assert.That(
                archivedEntry.TotalContentCharacters,
                Is.EqualTo(long.Parse(expected["total characters"])));
            Assert.That(
                archivedEntry.ArchivedPrefixCharacters,
                Is.EqualTo(archivedPrefix));
            Assert.That(
                archivedEntry.Entry!.Content,
                Has.Length.EqualTo(
                    int.Parse(expected["archived characters"])));
            Assert.That(
                retainedEntry.Entry.Content,
                Does.EndWith(content[(int)retainedOffset..]));
            Assert.That(
                archivedEntry.Entry.Content,
                Does.StartWith(content[..archivedPrefix]));
        });
    }

    [Then("live transcript for {string} excludes entry {int}")]
    public void ThenLiveTranscriptExcludesEntry(string role, int entryIndex) =>
        Assert.That(
            myViewModel.CreateTranscriptSnapshot(500)
                .Single(item => item.Role == role)
                .Entries,
            Has.None.Property("EntryIndex").EqualTo(entryIndex));

    [Then("archived entry {int} for {string} has sequence {long} and content {string}")]
    public void ThenArchivedEntryHasSequenceAndContent(
        int entryIndex,
        string role,
        long sequence,
        string content)
    {
        var archivedEntry = myViewModel.CreateArchivedTranscriptEntry(
            role,
            entryIndex);
        Assert.Multiple(() =>
        {
            Assert.That(archivedEntry.Sequence, Is.EqualTo(sequence));
            Assert.That(archivedEntry.Entry?.Content, Is.EqualTo(content));
            Assert.That(archivedEntry.ContentTruncated, Is.False);
            Assert.That(
                archivedEntry.TotalContentCharacters,
                Is.EqualTo(content.Length));
            Assert.That(
                archivedEntry.ArchivedPrefixCharacters,
                Is.EqualTo(content.Length));
        });
    }

    [Then("unavailable archived entry {int} for {string} has sequence {long}")]
    public void ThenUnavailableArchivedEntryHasSequence(
        int entryIndex,
        string role,
        long sequence)
    {
        var archivedEntry = myViewModel.CreateArchivedTranscriptEntry(
            role,
            entryIndex);
        Assert.Multiple(() =>
        {
            Assert.That(archivedEntry.Sequence, Is.EqualTo(sequence));
            Assert.That(archivedEntry.Entry, Is.Null);
            Assert.That(archivedEntry.ContentTruncated, Is.False);
            Assert.That(archivedEntry.TotalContentCharacters, Is.Zero);
            Assert.That(archivedEntry.ArchivedPrefixCharacters, Is.Zero);
        });
    }

    [Then("archived transcript history for {string} contains {int} entries and reports truncation")]
    public void ThenArchivedTranscriptHistoryContainsEntriesAndReportsTruncation(
        string role,
        int entryCount)
    {
        var page = myViewModel.CreateTranscriptPage(role, int.MaxValue, 200);
        Assert.Multiple(() =>
        {
            Assert.That(page.Entries, Has.Count.EqualTo(entryCount));
            Assert.That(page.HistoryTruncated, Is.True);
            Assert.That(
                Directory.GetFiles(
                    myTranscriptHistoryDirectory!,
                    "*.json",
                    SearchOption.AllDirectories),
                Has.Length.EqualTo(entryCount));
            Assert.That(
                Directory.GetFiles(
                    myTranscriptHistoryDirectory!,
                    "*.txt",
                    SearchOption.AllDirectories),
                Has.Length.EqualTo(entryCount));
        });
    }

    [Then("transcript synchronization for {string} reports older history")]
    public void ThenTranscriptSynchronizationReportsOlderHistory(string role)
    {
        var snapshot = myViewModel.CreateTranscriptSnapshot(500)
            .Single(item => item.Role == role);
        Assert.That(snapshot.HasMore, Is.True);
    }

    [Then("archived transcript history for {string} contains a truncation marker")]
    public void ThenArchivedTranscriptHistoryContainsATruncationMarker(string role)
    {
        var page = myViewModel.CreateTranscriptPage(role, int.MaxValue, 200);
        var archivedEntry = myViewModel.CreateArchivedTranscriptEntry(
            role,
            page.Entries.Single().EntryIndex);
        Assert.Multiple(() =>
        {
            Assert.That(page.Entries.Single().Entry.Content, Does.Contain("content truncated"));
            Assert.That(page.HistoryTruncated, Is.True);
            Assert.That(archivedEntry.ContentTruncated, Is.True);
            Assert.That(
                archivedEntry.TotalContentCharacters,
                Is.GreaterThan(archivedEntry.ArchivedPrefixCharacters));
        });
    }

    [Then("synchronized transcript history for {string} reports unavailable archived content within {int} characters")]
    public void ThenSynchronizedTranscriptHistoryReportsUnavailableArchivedContent(
        string role,
        int maxCharacters)
    {
        var entry = myViewModel.CreateTranscriptSnapshot(500)
            .Single(item => item.Role == role)
            .Entries
            .Single(item => item.EntryIndex == 0);
        Assert.Multiple(() =>
        {
            Assert.That(entry.HasArchivedContent, Is.False);
            Assert.That(entry.Entry.Content, Does.Contain("no longer"));
            Assert.That(entry.Entry.Content, Does.Not.Contain("is available in transcript history"));
            Assert.That(entry.Entry.Content.Length, Is.LessThanOrEqualTo(maxCharacters));
        });
    }

    [When("the bounded ViewModel is disposed")]
    public async Task WhenTheBoundedViewModelIsDisposed() =>
        await myViewModel.DisposeAsync();

    [Then("its temporary transcript history is removed")]
    public void ThenItsTemporaryTranscriptHistoryIsRemoved() =>
        Assert.That(Directory.Exists(myTranscriptHistoryDirectory), Is.False);

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

    [Then("the application ViewModel role {string} saw one event")]
    public void ThenTheApplicationViewModelRoleSawOneEvent(string role) => Assert.That(myApplication!.ViewModel.Roles[role].EventCount, Is.EqualTo(1));

    [Then("the application ViewModel role {string} has error {string}")]
    public void ThenTheApplicationViewModelRoleHasError(string role, string message)
    {
        myWorkspace.WaitUntil(() => myApplication!.ViewModel.Roles[role].Error == message, "SDK-shaped session error");
        Assert.That(myApplication!.ViewModel.Roles[role].Status, Is.EqualTo("error"));
    }

    [Then("the application ViewModel has no pending interactions for {string}")]
    public void ThenTheApplicationViewModelHasNoPendingInteractionsFor(string role)
    {
        Assert.Multiple(() =>
        {
            Assert.That(myApplication!.ViewModel.PendingPermissions, Has.None.Property("Role").EqualTo(role));
            Assert.That(myApplication.ViewModel.PendingInputs, Has.None.Property("Role").EqualTo(role));
            Assert.That(myApplication.ViewModel.PendingElicitations, Has.None.Property("Role").EqualTo(role));
        });
    }

    [Then("the application recording {string} session received prompt {string}")]
    public void ThenTheApplicationRecordingSessionReceivedPrompt(string role, string prompt) =>
        Assert.That(
            myBackend.Sessions.Single(session => session.Role == role).Sends,
            Does.Contain(prompt));

    [Then("the recording application sessions are drained")]
    public void ThenTheRecordingApplicationSessionsAreDrained() => Assert.That(myApplication!.Sessions, Is.Empty);

    [Then("prepared role {string} is initialized before readiness publication")]
    public async Task ThenPreparedRoleIsInitializedBeforeReadinessPublication(string role)
    {
        var readinessProvider = myRecordingHostLease!.AgentReadinessProvider;

        Assert.Multiple(() =>
        {
            Assert.That(myApplication!.ViewModel.Roles.Keys, Does.Contain(role));
            Assert.That(readinessProvider, Is.Not.Null);
        });
        Assert.That(await readinessProvider!(role, CancellationToken.None), Is.Not.Null);
        myLifecycleTrace!.AssertOrdered("roles.populated", "roles.initialized");
        myLifecycleTrace.AssertOrdered("roles.initialized", "readinessProvider.installed");
        myLifecycleTrace.AssertOrdered("readinessProvider.installed", "application.ready");
    }

    [Then("the lifecycle trace records the current startup order")]
    public void ThenTheLifecycleTraceRecordsTheCurrentStartupOrder()
    {
        var milestones = new[]
        {
            "postLockPreparation.started",
            "roles.populated",
            "backend.prepared",
            "postLockPreparation.completed",
            "sleepInhibitor.started",
            "roles.initialized",
            "readinessProvider.installed",
            "workspace.prepared",
            "window.started",
            "backend.runtimeCreated",
            "backend.sessionRegistered:coder",
            "window.sessionsStarted",
            "handoff.recovered",
            "handoff.started",
            "application.ready",
        };

        foreach (var pair in milestones.Zip(milestones.Skip(1)))
            myLifecycleTrace!.AssertOrdered(pair.First, pair.Second);
    }

    [Then("the lifecycle trace shows the process-wide window starting before backend generation startup")]
    public void ThenTheLifecycleTraceShowsWindowBeforeBackendGenerationStartup() =>
        myLifecycleTrace!.AssertOrdered("window.started", "backend.sessionRegistered:coder");

    [Then("the lifecycle trace shows session registration completing before the window is told sessions started")]
    public void ThenTheLifecycleTraceShowsSessionRegistrationBeforeSessionsStarted() =>
        myLifecycleTrace!.AssertOrdered("backend.sessionRegistered:coder", "window.sessionsStarted");

    [Then("the lifecycle trace shows handoff recovery and production starting only after sessions are available")]
    public void ThenTheLifecycleTraceShowsHandoffAfterSessionsAvailable()
    {
        myLifecycleTrace!.AssertOrdered("window.sessionsStarted", "handoff.recovered");
        myLifecycleTrace.AssertOrdered("handoff.recovered", "handoff.started");
    }

    [Then("the lifecycle trace shows shutdown stopping handoff production before retiring the backend generation")]
    public void ThenTheLifecycleTraceShowsHandoffStopBeforeBackendRetirement() =>
        myLifecycleTrace!.AssertOrdered("handoff.stopped", "backend.disposed");

    [Then("the lifecycle trace shows session completion resolving before observer retirement completes")]
    public void ThenTheLifecycleTraceShowsSessionCompletionBeforeObserverRetirement() =>
        // SquadApplication only reaches backend disposal after AwaitEventTasksAsync drains every per-session
        // observer, and each observer's own await session.Completion cannot resolve until this session's dispose
        // runs. So backend.disposed is a genuine, non-racy proxy for observer retirement, driven by real
        // production cleanup rather than a test-manufactured signal.
        myLifecycleTrace!.AssertOrdered("session.coder.completionResolved", "backend.disposed");

    [Then("the lifecycle trace shows generation teardown finishing before the window and remaining process-wide resources release")]
    public void ThenTheLifecycleTraceShowsGenerationTeardownBeforeProcessWideRelease()
    {
        myLifecycleTrace!.AssertOrdered("backend.disposed", "window.stopped");
        myLifecycleTrace.AssertOrdered("backend.disposed", "window.disposed");
        myLifecycleTrace.AssertOrdered("backend.disposed", "sleepInhibitor.disposed");
    }

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

    [Then("the pending handoff wakes the registered recipient after terminal sessions start")]
    public void ThenThePendingHandoffWakesTheRegisteredRecipientAfterTerminalSessionsStart()
    {
        var recipient = myBackend.Sessions.Single(session => session.Role == "reviewer");
        myWorkspace.WaitUntil(() => recipient.Sends.Contains("You have new handoff mail. If idle, run squad ready-for-next."), "in-process recipient wake-up");
        myWorkspace.WaitUntil(() => myApplication!.ViewModel.Roles["reviewer"].TranscriptEntries.Any(entry => entry.Source == "harness" && entry.Content == "You have new handoff mail. If idle, run squad ready-for-next."), "in-process recipient harness transcript");
        Assert.Multiple(() =>
        {
            Assert.That(myRecipientWasUnnotifiedBeforePolling, Is.True);
            Assert.That(recipient.SendOrder, Is.EqualTo(new[] { "busy", "You have new handoff mail. If idle, run squad ready-for-next." }));
        });
    }

    [Then("the in-process recipient session had no overlapping sends")]
    public void ThenTheInProcessRecipientSessionHadNoOverlappingSends() =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == "reviewer").OverlappedSend, Is.False);

    [Then("the in-process handoff is archived and the notification failure is logged")]
    public void ThenTheInProcessHandoffIsArchivedAndTheNotificationFailureIsLogged()
    {
        var sent = Path.Combine(myApplicationRoot, ".blaxquad", "handoffs", "sent");
        myWorkspace.WaitUntil(() => Directory.Exists(sent) && Directory.EnumerateFiles(sent, "*.handoff").Any(), "in-process handoff archival");
        myWorkspace.WaitUntil(() => myInProcessHandoffLog.Any(entry => entry.StartsWith("notify-failed reviewer ", StringComparison.Ordinal)), "in-process notification failure");
    }

    [Then("the recovered inbox work is unchanged and wakes its recipient once")]
    public void ThenTheRecoveredInboxWorkIsUnchangedAndWakesItsRecipientOnce()
    {
        var recipient = myBackend.Sessions.Single(session => session.Role == "reviewer");
        myWorkspace.WaitUntil(() => recipient.Sends.Count == 1, "recovery wake-up");
        Assert.Multiple(() =>
        {
            Assert.That(myRecoveredInboxFiles.All(file => File.ReadAllText(file.Path) == file.Content), Is.True);
            Assert.That(recipient.Sends, Is.EqualTo(new[] { "You have new handoff mail. If idle, run squad ready-for-next." }));
        });
    }

    [Then("ViewModel role {string} has no error")]
    public void ThenViewModelRoleHasNoError(string role) => Assert.That(myViewModel.Roles[role].Error, Is.Null);

    [Then("ViewModel role {string} is working")]
    public void ThenViewModelRoleIsWorking(string role) => Assert.That(myViewModel.Roles[role].IsWorking, Is.True);

    [Then("ViewModel role {string} is not working")]
    public void ThenViewModelRoleIsNotWorking(string role) => Assert.That(myViewModel.Roles[role].IsWorking, Is.False);

    [Then("ViewModel role {string} is ready for a prompt")]
    public void ThenViewModelRoleIsReadyForAPrompt(string role) => Assert.That(myViewModel!.GetRoleReadiness(role), Is.True);

    [Then("ViewModel role {string} is not ready for a prompt")]
    public void ThenViewModelRoleIsNotReadyForAPrompt(string role) => Assert.That(myViewModel!.GetRoleReadiness(role), Is.False);

    [When("the ViewModel begins stopping")]
    public void WhenTheViewModelBeginsStopping() => myViewModel.BeginStopping();

    [Then("ViewModel role {string} transcript has a {string} entry {string}")]
    public void ThenViewModelRoleTranscriptHasEntry(string role, string source, string content) =>
        Assert.That(myViewModel.Roles[role].TranscriptEntries.Any(entry => entry.Source == source && entry.Content == content), Is.True);

    [Then("ViewModel role {string} transcript has a {string} entry:")]
    public void ThenViewModelRoleTranscriptHasDocStringEntry(string role, string source, string content) =>
        Assert.That(
            myViewModel.Roles[role].TranscriptEntries.Any(entry =>
                entry.Source == source &&
                NormalizeLineEndings(entry.Content) == NormalizeLineEndings(content)),
            Is.True);

    [Then("ViewModel role {string} transcript has exactly {int} {string} entry")]
    [Then("ViewModel role {string} transcript has exactly {int} {string} entries")]
    public void ThenViewModelRoleTranscriptHasExactlyEntries(string role, int count, string source) =>
        Assert.That(myViewModel.Roles[role].TranscriptEntries.Count(entry => entry.Source == source), Is.EqualTo(count));

    [Then("ViewModel role {string} transcript has no entry {string}")]
    public void ThenViewModelRoleTranscriptHasNoEntry(string role, string content) =>
        Assert.That(myViewModel.Roles[role].TranscriptEntries.Any(entry => entry.Content == content), Is.False);

    [Then("ViewModel role {string} transcript has a decoded PowerShell command")]
    public void ThenViewModelRoleTranscriptHasADecodedPowerShellCommand(string role) =>
        Assert.That(
            ToolTranscriptEntry(role),
            Is.EqualTo("powershell Get-ChildItem -Path \"C:\\work\""));

    [Then("ViewModel role {string} transcript has raw glob arguments")]
    public void ThenViewModelRoleTranscriptHasRawGlobArguments(string role) =>
        Assert.That(
            ToolTranscriptEntry(role),
            Is.EqualTo("glob {\"pattern\":\"**/*\",\"paths\":\"C:\\\\work\"}"));

    private string ToolTranscriptEntry(string role) =>
        myViewModel.Roles[role].TranscriptEntries.Single(entry => entry.Source == "tool").Content;

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    [Then("ViewModel has one pending permission {string}")]
    public void ThenViewModelHasOnePendingPermission(string requestId) => Assert.That(myViewModel.PendingPermissions.Single().RequestId, Is.EqualTo(requestId));

    [Then("ViewModel has {int} pending permissions {string}")]
    public void ThenViewModelHasNPendingPermissions(int count, string requestId) =>
        Assert.That(myViewModel.PendingPermissions.Count(permission => permission.RequestId == requestId), Is.EqualTo(count));

    [Then("ViewModel has no pending permission")]
    public void ThenViewModelHasNoPendingPermission() => Assert.That(myViewModel.PendingPermissions, Is.Empty);

    [Then("ViewModel has one pending input {string}")]
    public void ThenViewModelHasOnePendingInput(string requestId) => Assert.That(myViewModel.PendingInputs.Single().RequestId, Is.EqualTo(requestId));

    [Then("ViewModel input {string} has choices {string}")]
    public void ThenViewModelInputHasChoices(string requestId, string choices) =>
        Assert.That(myViewModel.PendingInputs.Single(input => input.RequestId == requestId).Choices, Is.EqualTo(choices.Split(',')));

    [Then("ViewModel has one pending elicitation {string}")]
    public void ThenViewModelHasOnePendingElicitation(string requestId) => Assert.That(myViewModel.PendingElicitations.Single().RequestId, Is.EqualTo(requestId));

    [Then("ViewModel elicitation {string} has mode {string}")]
    public void ThenViewModelElicitationHasMode(string requestId, string mode) =>
        Assert.That(myViewModel.PendingElicitations.Single(elicitation => elicitation.RequestId == requestId).Mode, Is.EqualTo(mode));

    [Then("ViewModel elicitation {string} has URL {string}")]
    public void ThenViewModelElicitationHasUrl(string requestId, string url) =>
        Assert.That(myViewModel.PendingElicitations.Single(elicitation => elicitation.RequestId == requestId).Url, Is.EqualTo(url));

    [Then("interaction completion is rejected")]
    public void ThenInteractionCompletionIsRejected() => Assert.That(myInteractionCompletionFailure, Is.TypeOf<InvalidOperationException>());

    [Then("the recording {string} session received approved permission {string}")]
    public void ThenRecordingSessionReceivedApprovedPermission(string role, string requestId) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).PermissionResponses.Any(response => response.RequestId == requestId && response.Response.Approved), Is.True);

    [Then("the recording {string} session received rejected permission {string}")]
    public void ThenRecordingSessionReceivedRejectedPermission(string role, string requestId) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).PermissionResponses.Any(response => response.RequestId == requestId && !response.Response.Approved), Is.True);

    [Then("the recording {string} session received input {string} with answer {string}")]
    public void ThenRecordingSessionReceivedInput(string role, string requestId, string answer) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).InputResponses.Any(response => response.RequestId == requestId && response.Response.Answer == answer && response.Response.WasFreeform), Is.True);

    [Then("the recording {string} session received accepted elicitation {string} with form value {string}")]
    public void ThenRecordingSessionReceivedAcceptedElicitation(string role, string requestId, string value) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).ElicitationResponses.Any(response => response.RequestId == requestId && response.Response.Action == "accept" && response.Response.Content!.Value.GetProperty("answer").GetString() == value), Is.True);

    [Then("the recording {string} session cancelled pending interactions")]
    public void ThenRecordingSessionCancelledPendingInteractions(string role) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).PendingInteractionCancellationCount, Is.GreaterThan(0));

    [Then("ViewModel role {string} has active tool {string}")]
    public void ThenViewModelRoleHasActiveTool(string role, string tool) => Assert.That(myViewModel.Roles[role].ActiveTool, Is.EqualTo(tool));

    [Then("ViewModel role {string} has no active tool")]
    public void ThenViewModelRoleHasNoActiveTool(string role) => Assert.That(myViewModel.Roles[role].ActiveTool, Is.Null);

    [Then("the recording {string} session received prompts {string}")]
    public void ThenRecordingSessionReceivedPrompts(string role, string prompts) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).Sends, Is.EqualTo(prompts.Split(',')));

    [Then("the recording {string} session received prompt {string}")]
    public void ThenRecordingSessionReceivedPrompt(string role, string prompt) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).Sends, Has.Some.EqualTo(prompt));

    [Then("the recording {string} session had no overlapping sends")]
    public void ThenRecordingSessionHadNoOverlappingSends(string role) =>
        Assert.That(myBackend.Sessions.Single(session => session.Role == role).OverlappedSend, Is.False);

    [Then("the reviewer prompt completed before the coder prompt")]
    public void ThenTheReviewerPromptCompletedBeforeTheCoderPrompt() =>
        Assert.Multiple(() =>
        {
            Assert.That(myBackend.Sessions.Single(session => session.Role == "reviewer").SendOrder, Is.EqualTo(new[] { "fast" }));
            Assert.That(myFastCompletedBeforeSlow, Is.True);
        });

    private async Task CompleteInteractionAsync(Func<Task> complete)
    {
        myInteractionCompletionFailure = null;
        try
        {
            await complete();
        }
        catch (Exception exception)
        {
            myInteractionCompletionFailure = exception;
        }
    }

    [Then("the recording {string} session has one abort")]
    public void ThenRecordingSessionHasOneAbort(string role) => Assert.That(myBackend.Sessions.Single(session => session.Role == role).AbortCount, Is.EqualTo(1));

    [Then("the recording {string} session has no abort")]
    public void ThenRecordingSessionHasNoAbort(string role) => Assert.That(myBackend.Sessions.Single(session => session.Role == role).AbortCount, Is.Zero);

    [Then("the active prompt was cancelled")]
    public void ThenTheActivePromptWasCancelled() => Assert.That(myInFlightApplicationCommand, Is.Not.Null.And.Property("IsCanceled").True);

    [Then("cancellation failed")]
    public void ThenCancellationFailed() => Assert.That(myAbortFailure, Is.Not.Null);

    [Then("the recording {string} session has two aborts")]
    public void ThenRecordingSessionHasTwoAborts(string role) => Assert.That(myBackend.Sessions.Single(session => session.Role == role).AbortCount, Is.EqualTo(2));

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
        myApplication = new SquadApplication(myApplicationContext!, new WorkspacePreparer(_ => { }), myBackend, myRecordingPump!, myRecordingWindow!, myRecordingSleep!, viewModel: myApplication!.ViewModel, hostLease: myApplicationLease);
    }

    private void ConfigureControllableApplication(bool blockStartup = false, bool useRealLease = false, bool faultServer = false)
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        myRecordingPump = new RecordingHandoffPump();
        myRecordingSleep = new RecordingSleepInhibitor { BlockStart = blockStartup };
        myRecordingHostLease = useRealLease ? null : new RecordingHostLease();
        myFaultingHostLease = null;
        if (useRealLease)
            myApplicationLease = HostLease.Acquire(myApplicationRoot);
        if (faultServer)
        {
            myApplicationLease = HostLease.Acquire(myApplicationRoot);
            myRecordingHostLease = null;
            myFaultingHostLease = new FaultingHostLease(myApplicationLease);
        }
        myApplication = new SquadApplication(
            myApplicationContext!,
            new WorkspacePreparer(_ => { }),
            myBackend,
            myRecordingPump,
            myRecordingWindow!,
            myRecordingSleep,
            viewModel: myApplication!.ViewModel,
            hostLease: (IHostLease?)myRecordingHostLease ?? (IHostLease?)myFaultingHostLease ?? myApplicationLease);
    }

    private void ConfigureInProcessHandoffApplication(string? unavailableRecipient = null)
    {
        GivenASquadApplicationWithRecordingRoles("coder,reviewer");
        if (unavailableRecipient == "missing")
            myBackend.RemoveRole("reviewer");
        else if (unavailableRecipient != null && unavailableRecipient is not ("stopped" or "failed"))
            throw new ArgumentOutOfRangeException(nameof(unavailableRecipient));

        var registry = new SessionRegistry();
        myInProcessHandoffLog.Clear();
        var roles = myApplicationContext!.Roles.Select(r => new RoleRow(r.Role, r.WorktreeName, r.WorktreePath, r.DisplayName, r.ReceiveMode)).ToArray();
        myInProcessHandoffPump = new InProcessHandoffPoller(
            roles,
            new SessionRoleNotifier(registry, myApplication!.ViewModel),
            parts => myInProcessHandoffLog.Enqueue(string.Join(" ", parts)));
        if (unavailableRecipient == "stopped")
            myRecordingWindow!.OnSessionsStarted = () => myBackend.Sessions.Single(session => session.Role == "reviewer").DisposeAsync().GetAwaiter().GetResult();
        if (unavailableRecipient == "failed")
            myRecordingWindow!.OnSessionsStarted = () => myBackend.Sessions.Single(session => session.Role == "reviewer").Fail("recording session failed");

        myApplication = new SquadApplication(
            myApplicationContext,
            new WorkspacePreparer(_ => { }),
            myBackend,
            myInProcessHandoffPump,
            myRecordingWindow!,
            myRecordingSleep!,
            viewModel: myApplication.ViewModel,
            sessionRegistry: registry);
    }

    private void WritePendingHandoff()
    {
        var path = Path.Combine(myApplicationRoot, ".blaxquad", "handoffs", "outbox", "pending.handoff");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "id: pending\nfrom: coder\nto: reviewer\npriority: 50\ntype: note\nmessage: Pending.\n\nPending.\n");
    }

    private void WriteRecoveredInboxWork(string state, string fileName)
    {
        var path = Path.Combine(myApplicationRoot, ".blaxquad", "handoffs", "inbox", state, fileName);
        var content = $"id: {fileName}\nfrom: coder\nto: reviewer\n\nRecovery work.\n";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        myRecoveredInboxFiles.Add((path, content));
    }

    private void AssertUiSnapshotInteraction(string collection, string requestId, string role)
    {
        var snapshot = myViewModel.CreateSnapshot();
        var interaction = snapshot.GetProperty(collection).EnumerateArray().Single(entry => entry.GetProperty("requestId").GetString() == requestId);
        Assert.That(interaction.GetProperty("role").GetString(), Is.EqualTo(role));
    }

    private void StartApplicationRun() => myApplicationRun = myApplication!.RunAsync(AnnounceReadinessAsync, myApplicationCancellation?.Token ?? default);

    private async Task BeginExternalShutdownAsync()
    {
        myExternalShutdown = HostControlClient.ShutdownAsync(myApplicationRoot, TimeSpan.FromSeconds(5));
        await myApplicationLease!.ShutdownRequested.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private async Task StartApplicationUntilReadyAsync()
    {
        StartApplicationRun();
        while (myApplicationReadyCount == 0 && !myApplicationRun!.IsCompleted)
            await Task.Delay(10);
        if (myApplicationReadyCount == 0)
            await CompleteApplicationRunAsync();
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
            return;
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
