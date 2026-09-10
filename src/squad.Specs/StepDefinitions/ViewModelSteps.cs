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
    private HostLease? myApplicationLease;
    private Ctx? myApplicationContext;
    private Task<RunResult>? myApplicationRun;
    private int myApplicationReadyCount;
    private RecordingHandoffPump? myRecordingPump;
    private RecordingSleepInhibitor? myRecordingSleep;
    private RecordingHostLease? myRecordingHostLease;
    private FaultingHostLease? myFaultingHostLease;
    private RunResult? myApplicationRunResult;
    private Exception? myApplicationLifecycleFailure;
    private readonly List<string> mySdkInstructionsSentAfterRegistration = [];
    private readonly List<TranscriptUpdate> myTranscriptUpdates = [];
    private readonly CopilotToolOutputNormalizer myToolOutputNormalizer = new();
    private readonly Dictionary<string, string> myActiveToolCallIds = new(StringComparer.Ordinal);
    private int myNextToolCallId;

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

    [When("the SquadApplication starts")]
    public async Task WhenTheSquadApplicationStarts() => await StartApplicationUntilReadyAsync();

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

    [Given("a controllable SquadApplication")]
    public void GivenAControllableSquadApplication() => ConfigureControllableApplication();

    [Given("a controllable SquadApplication with blocked startup")]
    public void GivenAControllableSquadApplicationWithBlockedStartup() => ConfigureControllableApplication(blockStartup: true);

    [Given("a controllable SquadApplication with blocked startup and a faulting server")]
    public void GivenAControllableSquadApplicationWithBlockedStartupAndAFaultingServer() =>
        ConfigureControllableApplication(blockStartup: true, faultServer: true);

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

    [Then("the application stopped after readiness")]
    public async Task ThenTheApplicationStoppedAfterReadiness()
    {
        await CompleteApplicationRunAsync();
        Assert.That(myApplicationRunResult, Is.EqualTo(RunResult.StoppedAfterReady));
    }

    [Then("readiness was announced once")]
    public void ThenReadinessWasAnnouncedOnce() => Assert.That(myApplicationReadyCount, Is.EqualTo(1));

    [Then("the application lifecycle failed with {string}")]
    public async Task ThenTheApplicationLifecycleFailedWith(string message)
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

    [When("the application window closes")]
    public void WhenTheApplicationWindowCloses() => myRecordingWindow!.Close();

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

    private void ConfigureControllableApplication(bool blockStartup = false, bool faultServer = false)
    {
        GivenASquadApplicationWithRecordingRoles("coder");
        myRecordingPump = new RecordingHandoffPump();
        myRecordingSleep = new RecordingSleepInhibitor { BlockStart = blockStartup };
        myRecordingHostLease = new RecordingHostLease();
        myFaultingHostLease = null;
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
        myApplicationReadyCount++;
        return Task.CompletedTask;
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
