using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Operator-facing language for launching, observing readiness of, and shutting down a real squad-hq process.
/// Requests the scenario's single <see cref="BackendScenario"/> instance rather than constructing its own, so
/// agent-session steps bound elsewhere observe the same running process. Owns only the replacement Headquarters
/// launch it creates itself; the shared scenario's own process remains <see cref="BackendScenarioSteps"/>'s
/// teardown responsibility.
/// </summary>
[Binding]
public sealed class HeadquartersLifecycleSteps
{
    private readonly BackendScenario myScenario;
    private BackendScenario? myReplacementHeadquarters;
    private int myExitCode;

    public HeadquartersLifecycleSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [AfterScenario]
    public void CleanUp() => myReplacementHeadquarters?.Dispose();

    [Given("role {string} has a durable file {string} containing {string}")]
    public void GivenRoleHasADurableFileContaining(string role, string relativePath, string content) =>
        myScenario.SeedDurableRoleFile(role, relativePath, content);

    [When("the operator launches Headquarters with the fake provider")]
    public void WhenTheOperatorLaunchesHeadquartersWithTheFakeProvider()
    {
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

    [When("the operator launches a new Headquarters against the same project with the echo provider")]
    public void WhenTheOperatorLaunchesANewHeadquartersAgainstTheSameProjectWithTheEchoProvider() =>
        myReplacementHeadquarters = Await(myScenario.StartReplacementAsync<EchoAgentProviderFactory>());

    [Then("the new Headquarters process reports ready")]
    public void ThenTheNewHeadquartersProcessReportsReady() =>
        Assert.That(myReplacementHeadquarters!.IsReady, Is.True);

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
