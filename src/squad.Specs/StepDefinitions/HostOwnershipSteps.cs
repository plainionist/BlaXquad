using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using global::squadHQ.Commands;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HostOwnershipSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private HostLease? myLease;
    private Task? myReleaseAfterShutdown;
    private Exception? mySecondAcquisitionFailure;
    private string? myInvalidResponse;
    private string? myPingResponse;
    private string? myMalformedResponse;
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

    [Given("stale host metadata exists")]
    public void GivenStaleHostMetadataExists()
    {
        myWorkspace.WriteFile(".blaxquad/host.json", "{ \"version\": 1, \"controlPipe\": \"stale\" }\n");
    }

    [When("the executable requests squad shutdown again")]
    public void WhenTheExecutableRequestsSquadShutdownAgain() => WhenTheExecutableRequestsSquadShutdown();

    [Then("the executable shutdown succeeds")]
    public void ThenTheExecutableShutdownSucceeds() => Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);

    [Then("host metadata is absent")]
    public void ThenHostMetadataIsAbsent() => Assert.That(File.Exists(myWorkspace.PathInWorkspace(".blaxquad", "host.json")), Is.False);

    [When("an invalid control request is sent")]
    public void WhenAnInvalidControlRequestIsSent()
    {
        using var pipe = new NamedPipeClientStream(".", myLease!.PipeName, PipeDirection.InOut);
        pipe.Connect(5000);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        writer.WriteLine("{\"version\":999,\"command\":\"shutdown\"}");
        myInvalidResponse = reader.ReadLine();
    }

    [When("a ping control request is sent")]
    public void WhenAPingControlRequestIsSent()
    {
        using var pipe = new NamedPipeClientStream(".", myLease!.PipeName, PipeDirection.InOut);
        pipe.Connect(5000);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        writer.WriteLine("{\"version\":1,\"command\":\"ping\"}");
        myPingResponse = reader.ReadLine();
    }

    [When("a malformed control request is sent")]
    public void WhenAMalformedControlRequestIsSent()
    {
        using var pipe = new NamedPipeClientStream(".", myLease!.PipeName, PipeDirection.InOut);
        pipe.Connect(5000);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        writer.WriteLine("not-json");
        myMalformedResponse = reader.ReadLine();
    }

    [Then("the invalid control request is rejected")]
    public void ThenTheInvalidControlRequestIsRejected() => Assert.That(myInvalidResponse, Does.Contain("error"));

    [Then("the control server remains available")]
    public void ThenTheControlServerRemainsAvailable() => Assert.That(myPingResponse, Does.Contain("ping"));

    [Then("the malformed control request is rejected")]
    public void ThenTheMalformedControlRequestIsRejected() => Assert.That(myMalformedResponse, Does.Contain("error"));

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

    [Then("the host lock can be reacquired")]
    public void ThenTheHostLockCanBeReacquired()
    {
        var lease = HostLease.Acquire(myWorkspace.Root);
        lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [When("a second project host lease is acquired")]
    public void WhenASecondProjectHostLeaseIsAcquired()
    {
        try
        {
            var second = HostLease.Acquire(myWorkspace.Root);
            second.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            mySecondAcquisitionFailure = exception;
        }
    }

    [When("the executable attempts a duplicate launch")]
    public void WhenTheExecutableAttemptsADuplicateLaunch() =>
        myWorkspace.RunTool("squad-hq", ["launch", myWorkspace.Root]);

    [When("the executable queries its native window title")]
    public void WhenTheExecutableQueriesItsNativeWindowTitle() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--test-window-title", myWorkspace.Root]);

    [Then("the executable reports launch selection {string}")]
    public void ThenTheExecutableReportsLaunchSelection(string selection)
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
            Assert.That(myWorkspace.LastResult?.StdOut.Trim(), Is.EqualTo(selection));
        });
    }

    [Then("the executable reports the native window title for its workspace")]
    public void ThenTheExecutableReportsTheNativeWindowTitleForItsWorkspace()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
            Assert.That(myWorkspace.LastResult?.StdOut.Trim(), Is.EqualTo($"BlaXquad - {myWorkspace.Root}"));
        });
    }

    [When("the executable queries a default context window with {long} prompt tokens and {long} output tokens")]
    public void WhenTheExecutableQueriesADefaultContextWindow(long promptTokens, long outputTokens) =>
        myWorkspace.RunTool("squad-hq", ["launch", "--test-context-window", promptTokens.ToString(), outputTokens.ToString()]);

    [Then("the executable reports a default context window of {long} tokens")]
    public void ThenTheExecutableReportsADefaultContextWindow(long expectedTokens)
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
            Assert.That(myWorkspace.LastResult?.StdOut.Trim(), Is.EqualTo(expectedTokens.ToString()));
        });
    }

    [When("the executable exercises its context window cache")]
    public void WhenTheExecutableExercisesItsContextWindowCache() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--test-context-window-cache"]);

    [Then("the executable reports one context window lookup")]
    public void ThenTheExecutableReportsOneContextWindowLookup()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
            Assert.That(myWorkspace.LastResult?.StdOut.Trim(), Is.EqualTo("1"));
        });
    }

    [When("the executable checks whether command {string} is available")]
    public void WhenTheExecutableChecksWhetherCommandIsAvailable(string command) =>
        myWorkspace.RunTool("squad-hq", ["launch", "--test-command-exists", command]);

    [Then("command availability is reported as {string}")]
    public void ThenCommandAvailabilityIsReported(string availability)
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
            Assert.That(myWorkspace.LastResult?.StdOut.Trim(), Is.EqualTo(availability));
        });
    }

    [Then("the duplicate launch fails without an exception trace")]
    public void ThenTheDuplicateLaunchFailsWithoutAnExceptionTrace()
    {
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("A squad host is already running"));
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Not.Contain("Unhandled exception"));
    }

    [Then("host metadata exists")]
    public void ThenHostMetadataExists() =>
        Assert.That(File.Exists(myWorkspace.PathInWorkspace(".blaxquad", "host.json")), Is.True);

    [Then("host metadata names the project root")]
    public void ThenHostMetadataNamesTheProjectRoot()
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(myWorkspace.PathInWorkspace(".blaxquad", "host.json")));
        Assert.That(metadata.RootElement.GetProperty("projectRoot").GetString(), Is.EqualTo(Path.GetFullPath(myWorkspace.Root)));
    }

    [Then("the second host acquisition fails")]
    public void ThenTheSecondHostAcquisitionFails() => Assert.That(mySecondAcquisitionFailure, Is.Not.Null);

    [Then("host metadata still exists")]
    public void ThenHostMetadataStillExists() => ThenHostMetadataExists();

    [AfterScenario]
    public async Task ReleaseHostLease()
    {
        if (myOrphanedHostLock is not null)
            myOrphanedHostLock.Dispose();
        if (myReleaseAfterShutdown is { IsCompleted: true })
            await myReleaseAfterShutdown;
        else if (myLease is not null)
            await myLease.DisposeAsync();
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
            arguments.Add(projectRoot);
        var stopwatch = Stopwatch.StartNew();
        myWorkspace.RunTool("squad-hq", arguments, workingDirectory: workingDirectory);
        myWaitElapsed = stopwatch.Elapsed;
    }
}




