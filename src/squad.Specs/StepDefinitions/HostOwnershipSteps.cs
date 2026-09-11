using System.Diagnostics;
using squad.Specs.Support;
using squad.Specs.Support.Agents;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Host-ownership and project-isolation language: a project has one authoritative host, `squad-hq launch` refuses
/// a duplicate launch while the original host keeps answering, and `squad-hq shutdown` / `squad-hq wait-for-agent`
/// resolve that same host from an equivalent path, a linked worktree, or the default checkout. Requests the
/// scenario's single <see cref="BackendScenario"/> instance rather than constructing its own, so its "a squad host
/// is running" / "a Git project host with a ready/busy ... agent" setup steps stand up the same process that the
/// shared Headquarters-lifecycle launch/shutdown/wait-for-agent vocabulary then drives - reusing that vocabulary
/// for shutdown and wait-for-agent outcomes here rather than a second dialect for the same real host-control
/// commands. Only the project-root-discovery contexts genuinely unique to this feature (an equivalent path, a
/// linked worktree, no explicit root, or a zero timeout) get their own steps, sharing the same
/// `squad-hq wait-for-agent` verb phrase.
/// </summary>
[Binding]
public sealed class HostOwnershipSteps
{
    private const string HostRole = "architect";

    private readonly ScenarioWorkspace myWorkspace;
    private readonly BackendScenario myScenario;
    private string? myLinkedWorktree;
    private TimeSpan myWaitElapsed;

    public HostOwnershipSteps(ScenarioWorkspace workspace, BackendScenario scenario)
    {
        myWorkspace = workspace;
        myScenario = scenario;
    }

    [Given("a squad host is running")]
    public async Task GivenASquadHostIsRunning()
    {
        myScenario.ConfigureRole(HostRole);
        myScenario.EnableFakeProviderControl();
        await myScenario.StartAsync<FakeAgentProviderFactory>();
        // The "duplicate launch fails clearly" scenario later sends a prompt and asserts on its echoed reply, so
        // this shared setup step arms auto-echo once the role's session has genuinely started - the same
        // fake-provider control pipe pattern every other prompt-echoing fixture in this suite uses - keeping
        // every scenario that reuses this step (most of which never send a prompt at all) unaffected.
        await myScenario.WaitForRoleSessionStartedAsync(HostRole);
        await myScenario.Agent(HostRole).EnableAutoEchoAsync();
    }

    [Given("a Git project host with a ready {string} agent")]
    public async Task GivenAGitProjectHostWithAReadyAgent(string role)
    {
        myScenario.ConfigureRole(role);
        myScenario.EnableFakeProviderControl();
        await myScenario.StartAsync<FakeAgentProviderFactory>();
        await myScenario.WaitForRoleSessionStartedAsync(role);
        await myScenario.Agent(role).EmitIdleAsync();
    }

    [Given("a Git project host with a busy {string} agent")]
    public async Task GivenAGitProjectHostWithABusyAgent(string role)
    {
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

    [When("the operator requests shutdown for an equivalent project path")]
    public void WhenTheOperatorRequestsShutdownForAnEquivalentProjectPath() =>
        myWorkspace.RunBackendSpecSquadHq(["shutdown", myWorkspace.Root + Path.DirectorySeparatorChar]);

    [When("the operator requests shutdown for the empty project")]
    public void WhenTheOperatorRequestsShutdownForTheEmptyProject() =>
        myWorkspace.RunTool("squad-hq", ["shutdown", myWorkspace.Root]);

    [When("the host process is abruptly terminated")]
    public void WhenTheHostProcessIsAbruptlyTerminated() => myScenario.Terminate();

    [Then("the operator's shutdown succeeds")]
    public void ThenTheOperatorSShutdownSucceeds() => Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);

    [When("the operator waits {double} seconds for role {string} to become ready with `squad-hq wait-for-agent`")]
    public void WhenTheOperatorWaitsForRoleToBecomeReadyWithSquadHqWaitForAgent(double timeoutSeconds, string role) =>
        RunTimedWait(
            role,
            timeoutSeconds,
            myWorkspace.Root);

    [When("the operator waits for role {string} to become ready with `squad-hq wait-for-agent` without an explicit project root")]
    public void WhenTheOperatorWaitsForRoleToBecomeReadyWithSquadHqWaitForAgentWithoutAnExplicitProjectRoot(string role) =>
        RunTimedWait(role, 2, projectRoot: null, myWorkspace.Root);

    [When("the operator waits for role {string} to become ready with `squad-hq wait-for-agent` from the linked worktree")]
    public void WhenTheOperatorWaitsForRoleToBecomeReadyWithSquadHqWaitForAgentFromTheLinkedWorktree(string role) =>
        RunTimedWait(role, 2, projectRoot: null, myLinkedWorktree);

    [When("the operator waits for role {string} to become ready with `squad-hq wait-for-agent` using an equivalent project path")]
    public void WhenTheOperatorWaitsForRoleToBecomeReadyWithSquadHqWaitForAgentUsingAnEquivalentProjectPath(string role) =>
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

    [When("the operator waits for role {string} to become ready with `squad-hq wait-for-agent` with a zero timeout")]
    public void WhenTheOperatorWaitsForRoleToBecomeReadyWithSquadHqWaitForAgentWithAZeroTimeout(string role) =>
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

    [When("the operator attempts a duplicate launch")]
    public void WhenTheOperatorAttemptsADuplicateLaunch() =>
        myWorkspace.RunTool("squad-hq", ["launch", myWorkspace.Root]);

    [Then("the duplicate launch fails without an exception trace")]
    public void ThenTheDuplicateLaunchFailsWithoutAnExceptionTrace()
    {
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain("A squad host is already running"));
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Not.Contain("Unhandled exception"));
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




