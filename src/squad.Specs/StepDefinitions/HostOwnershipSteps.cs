using System.Diagnostics;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HostOwnershipSteps
{
    private const string HostRole = "architect";

    private readonly ScenarioWorkspace myWorkspace;
    private BackendScenario? myScenario;
    private BackendScenario? myReplacementScenario;
    private BackendScenarioCommand? myWaitCommand;
    private string? myLinkedWorktree;
    private TimeSpan myWaitElapsed;

    public HostOwnershipSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a squad host is running")]
    public async Task GivenASquadHostIsRunning()
    {
        myScenario = new BackendScenario(myWorkspace);
        myScenario.ConfigureRole(HostRole);
        await myScenario.StartAsync<EchoAgentProviderFactory>();
    }

    [Given("a Git project host with a ready {string} agent")]
    public async Task GivenAGitProjectHostWithAReadyAgent(string role)
    {
        myScenario = new BackendScenario(myWorkspace);
        myScenario.ConfigureRole(role);
        myScenario.EnableFakeProviderControl();
        await myScenario.StartAsync<FakeAgentProviderFactory>();
        await myScenario.WaitForRoleSessionStartedAsync(role);
        await myScenario.Agent(role).EmitIdleAsync();
    }

    [Given("a Git project host with a busy {string} agent")]
    public async Task GivenAGitProjectHostWithABusyAgent(string role)
    {
        myScenario = new BackendScenario(myWorkspace);
        myScenario.ConfigureRole(role);
        myScenario.EnableFakeProviderControl();
        await myScenario.StartAsync<FakeAgentProviderFactory>();
        await myScenario.WaitForRoleSessionStartedAsync(role);
        await myScenario.Agent(role).EmitReadinessAsync("busy");
    }

    [Given("an {string} linked worktree")]
    public void GivenALinkedWorktree(string role)
    {
        myLinkedWorktree = myWorkspace.PathInWorkspace(".worktrees", role);
        var result = myWorkspace.RunGit("worktree", "add", "--detach", myLinkedWorktree, "HEAD");
        Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
    }

    [When("the executable requests squad shutdown")]
    public async Task WhenTheExecutableRequestsSquadShutdown() => await myScenario!.ShutdownAsync();

    [When("the executable requests shutdown for an equivalent project path")]
    public void WhenTheExecutableRequestsShutdownForAnEquivalentProjectPath() =>
        myWorkspace.RunBackendSpecSquadHq(["shutdown", myWorkspace.Root + Path.DirectorySeparatorChar]);

    [When("the executable requests shutdown for the empty project")]
    public void WhenTheExecutableRequestsShutdownForTheEmptyProject() =>
        myWorkspace.RunTool("squad-hq", ["shutdown", myWorkspace.Root]);

    [When("the host process is abruptly terminated")]
    public void WhenTheHostProcessIsAbruptlyTerminated() => myScenario!.Terminate();

    [Then("the host process exits")]
    public void ThenTheHostProcessExits() =>
        myWorkspace.WaitUntil(() => !myScenario!.IsRunning, "the host process to exit");

    [Then("the original host still answers a public command")]
    public async Task ThenTheOriginalHostStillAnswersAPublicCommand()
    {
        myScenario!.SendPrompt(HostRole, "still there?");
        await myScenario.WaitForTranscriptAsync(HostRole, "echo: still there?");
    }

    [Then("a new host can be started for the same project")]
    public async Task ThenANewHostCanBeStartedForTheSameProject()
    {
        myReplacementScenario = new BackendScenario(myWorkspace);
        await myReplacementScenario.StartAsync<EchoAgentProviderFactory>();
    }

    [Then("the executable shutdown succeeds")]
    public void ThenTheExecutableShutdownSucceeds() => Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);

    [When("the executable begins waiting for the {string} agent")]
    public void WhenTheExecutableBeginsWaitingForTheAgent(string role) =>
        myWaitCommand = myScenario!.StartWaitForAgent(role, TimeSpan.FromSeconds(5));

    [Then("the executable remains waiting for agent readiness")]
    public void ThenTheExecutableRemainsWaitingForAgentReadiness()
    {
        Thread.Sleep(200);
        Assert.That(myWaitCommand!.IsRunning, Is.True);
    }

    [When("the {string} agent becomes ready")]
    public async Task WhenTheAgentBecomesReady(string role) => await myScenario!.Agent(role).EmitIdleAsync();

    [Then("the agent readiness wait succeeds")]
    public async Task ThenTheAgentReadinessWaitSucceeds()
    {
        var result = await myWaitCommand!.WaitForCompletionAsync(TimeSpan.FromSeconds(10));
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
            Assert.That(result.StdOut, Does.Contain("is ready"));
        });
    }

    [When("the executable waits {double} seconds for the {string} agent")]
    public void WhenTheExecutableWaitsForTheAgent(double timeoutSeconds, string role) =>
        RunTimedWait(
            role,
            timeoutSeconds,
            myWorkspace.Root);

    [When("the executable waits for {string} without an explicit project root")]
    public void WhenTheExecutableWaitsWithoutAnExplicitProjectRoot(string role) =>
        RunTimedWait(role, 2, projectRoot: null, myWorkspace.Root);

    [When("the executable waits for {string} from the linked worktree")]
    public void WhenTheExecutableWaitsFromTheLinkedWorktree(string role) =>
        RunTimedWait(role, 2, projectRoot: null, myLinkedWorktree);

    [When("the executable waits for {string} using an equivalent project path")]
    public void WhenTheExecutableWaitsUsingAnEquivalentProjectPath(string role) =>
        RunTimedWait(role, 2, myWorkspace.Root + Path.DirectorySeparatorChar);

    [Then("the agent readiness wait times out")]
    public void ThenTheAgentReadinessWaitTimesOut()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("did not become ready"));
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("agent not ready"));
        });
    }

    [Then("the agent readiness wait reports an unknown role")]
    public void ThenTheAgentReadinessWaitReportsAnUnknownRole()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("no agent role named"));
        });
    }

    [Then("the agent readiness command succeeds")]
    public void ThenTheAgentReadinessCommandSucceeds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
            Assert.That(myWorkspace.LastResult?.StdOut, Does.Contain("is ready"));
        });
    }

    [Then("project root discovery fails promptly")]
    public void ThenProjectRootDiscoveryFailsPromptly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("Cannot find squad project root"));
            Assert.That(myWaitElapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        });
    }

    [When("the executable waits with a zero timeout for {string}")]
    public void WhenTheExecutableWaitsWithAZeroTimeout(string role) =>
        myWorkspace.RunBackendSpecSquadHq(["wait-for-agent", role, "--timeout", "0"]);

    [Then("the zero readiness timeout is rejected")]
    public void ThenTheZeroReadinessTimeoutIsRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("positive number of seconds"));
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Not.Contain("Cannot find squad project root"));
        });
    }

    [Then("the readiness wait reports the host as unavailable")]
    public void ThenTheReadinessWaitReportsTheHostAsUnavailable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("did not become ready"));
            Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("squad host unavailable"));
            Assert.That(myWaitElapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
        });
    }

    [When("the executable attempts a duplicate launch")]
    public void WhenTheExecutableAttemptsADuplicateLaunch() =>
        myWorkspace.RunTool("squad-hq", ["launch", myWorkspace.Root]);

    [Then("the duplicate launch fails without an exception trace")]
    public void ThenTheDuplicateLaunchFailsWithoutAnExceptionTrace()
    {
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("A squad host is already running"));
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Not.Contain("Unhandled exception"));
    }

    [AfterScenario]
    public void DisposeBackendScenarios()
    {
        myScenario?.Dispose();
        myReplacementScenario?.Dispose();
    }

    private void RunTimedWait(
        string role,
        double timeoutSeconds,
        string? projectRoot,
        string? workingDirectory = null)
    {
        var arguments = new List<string>
        {
            "wait-for-agent",
            role,
            "--timeout",
            timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (projectRoot is not null)
        {
            arguments.Add(projectRoot);
        }
        var stopwatch = Stopwatch.StartNew();
        myWorkspace.RunBackendSpecSquadHq(arguments, workingDirectory: workingDirectory);
        myWaitElapsed = stopwatch.Elapsed;
    }
}




