using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Operator-facing language for launching, observing readiness of, and shutting down a real squad-hq process,
/// including the asynchronous "begins waiting" / "remains pending" / "succeeds" form of
/// `squad-hq wait-for-agent` used to prove readiness follows a role's own idle and busy transitions rather than
/// merely a synchronous confirmation. Requests the scenario's single <see cref="BackendScenario"/> instance rather
/// than constructing its own, so agent-session steps bound elsewhere observe the same running process. Owns only
/// the replacement Headquarters launch it creates itself; the shared scenario's own process remains
/// <see cref="BackendScenarioSteps"/>'s teardown responsibility.
/// </summary>
[Binding]
public sealed class HeadquartersLifecycleSteps
{
    private readonly BackendScenario myScenario;
    private BackendScenario? myReplacementHeadquarters;
    private int myExitCode;
    private readonly Dictionary<string, BackendScenarioCommand> myReadinessWaits = new(StringComparer.Ordinal);

    public HeadquartersLifecycleSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [AfterScenario]
    public void CleanUp() => myReplacementHeadquarters?.Dispose();

    [Given("role {string} has a durable file {string} containing {string}")]
    public void GivenRoleHasADurableFileContaining(string role, string relativePath, string content) =>
        myScenario.SeedDurableRoleFile(role, relativePath, content);

    [When("the operator launches Headquarters")]
    public void WhenTheOperatorLaunchesHeadquarters()
    {
        // The fake provider and its control transport are test setup, not specified behavior: this scenario
        // proves session start/disposal and readiness through the real "squad-hq" surface, so the choice of
        // provider fixture stays behind this binding rather than becoming a second Gherkin dialect.
        myScenario.EnableFakeProviderControl();
        Await(myScenario.StartAsync<FakeAgentProviderFactory>());
    }

    [Then("Headquarters starts an agent session for role {string}")]
    public void ThenHeadquartersStartsAnAgentSessionForRole(string role) =>
        Await(myScenario.WaitForRoleSessionStartedAsync(role));

    [Then("Headquarters disposes the agent session for role {string}")]
    public void ThenHeadquartersDisposesTheAgentSessionForRole(string role) =>
        Await(myScenario.WaitForRoleSessionDisposedAsync(role));

    [Then("the operator confirms role {string} is ready with `squad-hq wait-for-agent`")]
    public void ThenTheOperatorConfirmsRoleIsReadyWithSquadHqWaitForAgent(string role)
    {
        var result = Await(myScenario.WaitForAgentReadyThroughCliAsync(role));
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
            Assert.That(result.StdOut, Does.Contain("is ready"));
        });
    }

    [When("the operator begins waiting for role {string} to become ready with `squad-hq wait-for-agent`")]
    public void WhenTheOperatorBeginsWaitingForRoleToBecomeReadyWithSquadHqWaitForAgent(string role) =>
        myReadinessWaits[role] = myScenario.StartWaitForAgent(role, TimeSpan.FromSeconds(10));

    [Then("role {string}'s readiness wait remains pending")]
    public async Task ThenRoleSReadinessWaitRemainsPending(string role)
    {
        // A short, independently bounded probe against the same live host proves the role is genuinely not ready
        // yet: it polls the host for its own full timeout before concluding "not ready", so its completion is
        // evidence of a live, contacted host currently reporting this role as not ready - not a guess about how
        // long a fixed sleep should be. This mirrors the equivalent proof in HostOwnershipSteps.
        var probe = myScenario.StartWaitForAgent(role, TimeSpan.FromSeconds(1));
        var probeResult = await probe.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
        Assert.That(probeResult.StdErr, Does.Contain("agent not ready"), () => probeResult.StdErr);
        Assert.That(myReadinessWaits[role].IsRunning, Is.True);
    }

    [Then("role {string}'s readiness wait succeeds")]
    public async Task ThenRoleSReadinessWaitSucceeds(string role)
    {
        var result = await myReadinessWaits[role].WaitForCompletionAsync(TimeSpan.FromSeconds(10));
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
            Assert.That(result.StdOut, Does.Contain("is ready"));
        });
    }

    [When("the operator shuts down Headquarters")]
    public void WhenTheOperatorShutsDownHeadquarters() =>
        myExitCode = Await(myScenario.ShutdownAsync());

    [Then("Headquarters exits with code {int}")]
    public void ThenHeadquartersExitsWithCode(int exitCode) =>
        Assert.That(myExitCode, Is.EqualTo(exitCode));

    [Then("the operator finds Headquarters unavailable for role {string}")]
    public void ThenTheOperatorFindsHeadquartersUnavailableForRole(string role)
    {
        var result = myScenario.ConfirmHostControlUnavailable(role);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.StdErr, Does.Contain("squad host unavailable"));
        });
    }

    [Then("role {string}'s durable file {string} still contains {string}")]
    public void ThenRoleSDurableFileStillContains(string role, string relativePath, string content) =>
        Assert.That(myScenario.DurableRoleFileIsPreserved(role, relativePath, content), Is.True);

    [When("the operator launches a new Headquarters against the same project")]
    public void WhenTheOperatorLaunchesANewHeadquartersAgainstTheSameProject() =>
        // A replacement launch is a child of the same scenario owner (StartReplacementAsync targets this
        // instance's own workspace), not an independent default facade; the echo provider is again a test-setup
        // choice kept out of Gherkin.
        myReplacementHeadquarters = Await(myScenario.StartReplacementAsync<EchoAgentProviderFactory>());

    [Then("the new Headquarters process reports ready")]
    public void ThenTheNewHeadquartersProcessReportsReady() =>
        Assert.That(myReplacementHeadquarters!.IsReady, Is.True);

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
