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
    private Task<int>? myPendingShutdown;
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

    [Given("Headquarters' startup pauses after {int} session has started")]
    public void GivenHeadquartersStartupPausesAfterSessionHasStarted(int count) =>
        myScenario.GateProviderStartupAfterSessions(count);

    [Given("Headquarters' provider fails before its runtime becomes available")]
    public void GivenHeadquartersProviderFailsBeforeItsRuntimeBecomesAvailable() =>
        myScenario.FailProviderBeforeRuntime();

    [Given("Headquarters' provider fails after {int} session has started")]
    public void GivenHeadquartersProviderFailsAfterSessionHasStarted(int count) =>
        myScenario.FailProviderAfterSessions(count);

    [Given("Headquarters' provider fails its cleanup with message {string}")]
    public void GivenHeadquartersProviderFailsItsCleanupWithMessage(string message) =>
        myScenario.FailProviderDisposal(message);

    [When("the operator launches Headquarters without completing the ready handshake")]
    public void WhenTheOperatorLaunchesHeadquartersWithoutCompletingTheReadyHandshake()
    {
        myScenario.EnableFakeProviderControl();
        myScenario.LaunchWithoutReadyHandshake<FakeAgentProviderFactory>();
    }

    [When("the operator launches a cancellable Headquarters")]
    public void WhenTheOperatorLaunchesACancellableHeadquarters()
    {
        myScenario.EnableFakeProviderControl();
        Await(myScenario.StartCancellableAsync<FakeAgentProviderFactory>());
    }

    [Then("Headquarters starts an agent session for role {string}")]
    public void ThenHeadquartersStartsAnAgentSessionForRole(string role) =>
        Await(myScenario.WaitForRoleSessionStartedAsync(role));

    [Then("Headquarters never starts an agent session for role {string}")]
    public void ThenHeadquartersNeverStartsAnAgentSessionForRole(string role) =>
        Assert.That(myScenario.RoleSessionNeverStarted(role), Is.True);

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

    [Then("role {string} was never reported ready")]
    public async Task ThenRoleWasNeverReportedReady(string role)
    {
        // The watch itself was started with its own 10s "wait-for-agent --timeout" bound; the extra margin here
        // only bounds how long a genuinely broken watch is allowed to hang before this assertion gives up.
        var result = await myReadinessWaits[role].WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        Assert.That(result.StdOut, Does.Not.Contain("is ready"), () => result.StdErr);
    }

    [When("the operator requests shutdown as soon as it is reachable")]
    public void WhenTheOperatorRequestsShutdownAsSoonAsItIsReachable() =>
        // Fired without awaiting completion, so a specification can compose a racing prompt send (through the
        // ordinary "the user sends ... to role ..." dashboard operation) before this request's own completion is
        // later observed through "Headquarters' pending shutdown completes".
        myPendingShutdown = myScenario.RequestShutdownAsSoonAsReachableAsync();

    [When("Headquarters' pending shutdown completes")]
    public void WhenHeadquartersPendingShutdownCompletes() =>
        myExitCode = Await(myPendingShutdown!);

    [When("the operator closes Headquarters' standard input")]
    public void WhenTheOperatorClosesHeadquartersStandardInput()
    {
        myScenario.CloseStandardInput();
        myExitCode = Await(myScenario.WaitForProcessExitAsync());
    }

    [When("the platform delivers its cancellation signal to Headquarters")]
    public void WhenThePlatformDeliversItsCancellationSignalToHeadquarters()
    {
        myScenario.RequestCallerCancellation();
        myExitCode = Await(myScenario.WaitForProcessExitAsync());
    }

    [When("the operator shuts down Headquarters")]
    public void WhenTheOperatorShutsDownHeadquarters() =>
        myExitCode = Await(myScenario.ShutdownAsync());

    // A composable alternative to "the operator shuts down Headquarters" for scenarios that must observe
    // in-flight cleanup (for example a held session disposal) before the process is allowed to exit: awaiting the
    // shutdown command's own completion, as the plain shutdown above does, would block up to its own timeout
    // waiting for host release and deadlock against that still-held state.
    [When("the operator begins shutting down Headquarters without waiting for it to exit")]
    public void WhenTheOperatorBeginsShuttingDownHeadquartersWithoutWaitingForItToExit() =>
        Await(myScenario.RequestShutdownWithoutWaitingForExit());

    [When("Headquarters' process exits on its own")]
    public void WhenHeadquartersSProcessExitsOnItsOwn() =>
        myExitCode = Await(myScenario.WaitForProcessExitAsync());

    [When("the agent provider fails its backend with message {string}")]
    public void WhenTheAgentProviderFailsItsBackendWithMessage(string message) =>
        Await(myScenario.FailProviderBackendAsync(message));

    [Then("Headquarters exits with code {int}")]
    public void ThenHeadquartersExitsWithCode(int exitCode) =>
        Assert.That(myExitCode, Is.EqualTo(exitCode));

    [Then("Headquarters exits with a non-zero code")]
    public void ThenHeadquartersExitsWithANonZeroCode() =>
        Assert.That(myExitCode, Is.Not.Zero);

    [Then("Headquarters' standard error contains {string}")]
    public void ThenHeadquartersSStandardErrorContains(string text) =>
        Await(myScenario.WaitForStandardErrorContainingAsync(text));

    [Then("Headquarters' standard error does not contain {string}")]
    public void ThenHeadquartersSStandardErrorDoesNotContain(string text) =>
        Assert.That(myScenario.CapturedStandardError(), Does.Not.Contain(text));

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

    // "Still available" reuses the same live probe as "unavailable": a genuinely dead host never answers at all
    // (its own exit code and "squad host unavailable" diagnostic), whereas a host that is merely still starting
    // its own role up (or, symmetrically, already shutting down but not yet released) answers reachably with its
    // own "agent not ready" diagnostic - proving the host itself is still owned and listening.
    [Then("the operator finds Headquarters still available for role {string}")]
    public void ThenTheOperatorFindsHeadquartersStillAvailableForRole(string role)
    {
        var result = myScenario.ConfirmHostControlUnavailable(role);
        Assert.That(result.StdErr, Does.Contain("agent not ready"), () => result.StdErr);
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
