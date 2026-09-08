using System.Diagnostics;
using squad.Host.Control;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HostOwnershipSteps
{
    private const string HostRole = "architect";

    private readonly ScenarioWorkspace myWorkspace;
    private HostLease? myLease;
    private Task? myReleaseAfterShutdown;
    private HeadlessUiClient? myHostClient;
    private System.Diagnostics.Process? myWaitProcess;
    private Task<string>? myWaitOutput;
    private Task<string>? myWaitError;
    private int myReadinessQueries;
    private bool myAgentReady;
    private string? myLinkedWorktree;
    private CleanupLease? myOrphanedHostLock;
    private TimeSpan myWaitElapsed;
    private Exception? myWaitFailure;

    public HostOwnershipSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a squad host is running")]
    public async Task GivenASquadHostIsRunning()
    {
        myWorkspace.ConfigureProject(HostRole);
        myHostClient = await myWorkspace.StartSquadHqHostAsync();
    }

    [Given("the project host lease is acquired")]
    public void GivenTheProjectHostLeaseIsAcquired()
    {
        myLease = HostLease.Acquire(myWorkspace.Root);
        myReleaseAfterShutdown = Task.Run(async () =>
        {
            await myLease.ShutdownRequested;
            await myLease.DisposeAsync();
        });
    }

    [Given("a Git project host with a ready {string} agent")]
    public void GivenAGitProjectHostWithAReadyAgent(string role)
    {
        myWorkspace.InitializeGitRepository();
        myWorkspace.WriteFile(
            "blaxquad/squad.json",
            $$"""
            {
              "roles": [
                { "name": "{{role}}", "worktree": "master", "agent": {} }
              ]
            }
            """ + "\n");
        GivenTheProjectHostLeaseIsAcquired();
        myLease!.SetAgentReadinessProvider(
            (requestedRole, _) => Task.FromResult<bool?>(requestedRole == role ? true : null));
    }

    [Given("an {string} linked worktree")]
    public void GivenALinkedWorktree(string role)
    {
        myLinkedWorktree = myWorkspace.PathInWorkspace(".worktrees", role);
        var result = myWorkspace.RunGit("worktree", "add", "--detach", myLinkedWorktree, "HEAD");
        Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
    }

    [When("the executable requests squad shutdown")]
    public void WhenTheExecutableRequestsSquadShutdown() =>
        myWorkspace.RunTool("squad-hq", ["shutdown", myWorkspace.Root]);

    [When("the executable requests shutdown for an equivalent project path")]
    public void WhenTheExecutableRequestsShutdownForAnEquivalentProjectPath() =>
        myWorkspace.RunTool("squad-hq", ["shutdown", myWorkspace.Root + Path.DirectorySeparatorChar]);

    [When("the executable requests shutdown for the empty project")]
    public void WhenTheExecutableRequestsShutdownForTheEmptyProject() =>
        myWorkspace.RunTool("squad-hq", ["shutdown", myWorkspace.Root]);

    [When("the host process is abruptly terminated")]
    public void WhenTheHostProcessIsAbruptlyTerminated()
    {
        myHostClient!.Terminate();
        Assert.That(myHostClient.WaitForExit(TimeSpan.FromSeconds(5)), Is.True);
    }

    [Then("the host process exits")]
    public void ThenTheHostProcessExits() =>
        Assert.That(myHostClient!.WaitForExit(TimeSpan.FromSeconds(10)), Is.True);

    [Then("the original host still answers a public command")]
    public async Task ThenTheOriginalHostStillAnswersAPublicCommand()
    {
        myHostClient!.SendPrompt(HostRole, "still there?");
        await myHostClient.WaitForTranscriptAsync(HostRole, "echo: still there?");
    }

    [Then("a new host can be started for the same project")]
    public async Task ThenANewHostCanBeStartedForTheSameProject() => await myWorkspace.StartSquadHqHostAsync();

    [Then("the executable shutdown succeeds")]
    public void ThenTheExecutableShutdownSucceeds() => Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);

    [Given("the {string} agent is not ready")]
    public void GivenTheAgentIsNotReady(string role)
    {
        myAgentReady = false;
        myLease!.SetAgentReadinessProvider((requestedRole, _) =>
        {
            Interlocked.Increment(ref myReadinessQueries);
            return Task.FromResult<bool?>(
                requestedRole == role ? Volatile.Read(ref myAgentReady) : null);
        });
    }

    [When("the executable begins waiting for the {string} agent")]
    public void WhenTheExecutableBeginsWaitingForTheAgent(string role)
    {
        myWaitProcess = myWorkspace.StartTool(
            "squad-hq",
            ["wait-for-agent", role, "--timeout", "5", myWorkspace.Root]);
        myWaitOutput = myWaitProcess.StandardOutput.ReadToEndAsync();
        myWaitError = myWaitProcess.StandardError.ReadToEndAsync();
    }

    [Then("the executable remains waiting for agent readiness")]
    public void ThenTheExecutableRemainsWaitingForAgentReadiness()
    {
        myWorkspace.WaitUntil(
            () => Volatile.Read(ref myReadinessQueries) > 0,
            "the readiness query to reach the host");
        Assert.That(myWaitProcess!.HasExited, Is.False);
    }

    [When("the {string} agent becomes ready")]
    public void WhenTheAgentBecomesReady(string role) => Volatile.Write(ref myAgentReady, true);

    [Then("the agent readiness wait succeeds")]
    public async Task ThenTheAgentReadinessWaitSucceeds()
    {
        await myWaitProcess!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Multiple(() =>
        {
            Assert.That(myWaitProcess.ExitCode, Is.Zero, () => myWaitError!.GetAwaiter().GetResult());
            Assert.That(myWaitOutput!.GetAwaiter().GetResult(), Does.Contain("is ready"));
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
        myWorkspace.RunTool("squad-hq", ["wait-for-agent", role, "--timeout", "0"]);

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

    [Given("the project host lock is held without a control server")]
    public void GivenTheProjectHostLockIsHeldWithoutAControlServer()
    {
        var stateDirectory = myWorkspace.PathInWorkspace(".blaxquad");
        Directory.CreateDirectory(stateDirectory);
        Assert.That(HostLease.TryAcquireCleanupLease(myWorkspace.Root, out myOrphanedHostLock), Is.True);
    }

    [Then("the unavailable control wait respects the deadline")]
    public void ThenTheUnavailableControlWaitRespectsTheDeadline()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWaitFailure, Is.TypeOf<TimeoutException>());
            Assert.That(myWaitFailure?.Message, Does.Contain("squad control endpoint unavailable"));
            Assert.That(myWaitElapsed, Is.LessThan(TimeSpan.FromSeconds(0.5)));
        });
    }

    [When("the host client waits {double} seconds for the {string} agent")]
    public async Task WhenTheHostClientWaitsForTheAgent(double timeoutSeconds, string role)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await HostControlClient.WaitForAgentAsync(
                myWorkspace.Root,
                role,
                TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (Exception exception)
        {
            myWaitFailure = exception;
        }
        myWaitElapsed = stopwatch.Elapsed;
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
    public async Task ReleaseHostLease()
    {
        if (myOrphanedHostLock is not null)
        {
            myOrphanedHostLock.Dispose();
        }
        if (myReleaseAfterShutdown is { IsCompleted: true })
        {
            await myReleaseAfterShutdown;
        }
        else if (myLease is not null)
        {
            await myLease.DisposeAsync();
        }
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
        myWorkspace.RunTool("squad-hq", arguments, workingDirectory: workingDirectory);
        myWaitElapsed = stopwatch.Elapsed;
    }
}




