using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives one backend-process specification exclusively through <see cref="BackendScenario"/> - the composition
/// root - never touching workspace paths, process handles, protocol DTOs, or product objects directly.
/// </summary>
[Binding]
public sealed class BackendScenarioSteps
{
    private readonly BackendScenario myScenario;
    private int myExitCode;

    public BackendScenarioSteps(ScenarioWorkspace workspace)
    {
        myScenario = new BackendScenario(workspace);
    }

    [Given("a backend scenario configured with a {string} role")]
    public void GivenABackendScenarioConfiguredWithARole(string role) => myScenario.ConfigureRole(role);

    [Given("the backend scenario has enabled the fake-provider control transport")]
    public void GivenTheBackendScenarioHasEnabledTheFakeProviderControlTransport() =>
        myScenario.EnableFakeProviderControl();

    [When("the backend scenario starts squad-hq with the echo provider fixture")]
    public void WhenTheBackendScenarioStartsSquadHqWithTheEchoProviderFixture() =>
        Await(myScenario.StartAsync<EchoAgentProviderFactory>());

    [When("the backend scenario starts squad-hq with the fake provider fixture")]
    public void WhenTheBackendScenarioStartsSquadHqWithTheFakeProviderFixture() =>
        Await(myScenario.StartAsync<FakeAgentProviderFactory>());

    [Then("the backend scenario reports the process as ready")]
    public void ThenTheBackendScenarioReportsTheProcessAsReady() =>
        Assert.That(myScenario.IsReady, Is.True);

    [Then("the backend scenario observes role {string} at status {string}")]
    public void ThenTheBackendScenarioObservesRoleAtStatus(string role, string status) =>
        Await(myScenario.WaitForRoleStatusAsync(role, status));

    [Then("the backend scenario observes a session started for role {string} across the control pipe")]
    public void ThenTheBackendScenarioObservesASessionStartedForRoleAcrossTheControlPipe(string role) =>
        Await(myScenario.WaitForRoleSessionStartedAsync(role));

    [Then("the backend scenario observes a session disposed for role {string} across the control pipe")]
    public void ThenTheBackendScenarioObservesASessionDisposedForRoleAcrossTheControlPipe(string role) =>
        Await(myScenario.WaitForRoleSessionDisposedAsync(role));

    [When("the backend scenario sends the prompt {string} to role {string}")]
    public void WhenTheBackendScenarioSendsThePromptToRole(string prompt, string role) =>
        myScenario.SendPrompt(role, prompt);

    [Then("the {string} agent observes the prompt {string}")]
    public void ThenTheAgentObservesThePrompt(string role, string expectedPrompt) =>
        Assert.That(Await(myScenario.Agent(role).WaitForPromptAsync()), Is.EqualTo(expectedPrompt));

    [When("the {string} agent replies with {string}")]
    public void WhenTheAgentRepliesWith(string role, string content) =>
        Await(myScenario.Agent(role).ReplyAsync(content));

    [Then("the backend scenario observes the transcript for role {string} containing {string}")]
    public void ThenTheBackendScenarioObservesTheTranscriptForRoleContaining(string role, string content) =>
        Await(myScenario.WaitForTranscriptAsync(role, content));

    [When("the backend scenario requests a host-control shutdown")]
    public void WhenTheBackendScenarioRequestsAHostControlShutdown() =>
        myExitCode = Await(myScenario.ShutdownAsync());

    [Then("the backend scenario observes an exit code of zero")]
    public void ThenTheBackendScenarioObservesAnExitCodeOfZero() =>
        Assert.That(myExitCode, Is.Zero);

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
