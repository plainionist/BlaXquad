using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the real, separately launched, published squad-hq "--ui stdio" process through the shared <see
/// cref="BackendScenario"/> process driver and the shared <see cref="FakeAgentProviderFactory"/> fixture, proving
/// <see cref="HeadlessUiClient"/>'s own semantic operations (readiness, prompt-sending, role-status, transcript,
/// and protocol-error) against a real published squad-hq process. No step here parses protocol envelopes, touches
/// raw JSON, reads a stream, or otherwise manages the child process directly; the "coder" role's automatic
/// "echo: {prompt}" reply crosses the shared fake-provider control pipe instead of a second, narrower provider
/// fixture.
/// </summary>
[Binding]
public sealed class HeadlessUiClientSteps
{
    private readonly BackendScenario myScenario;

    public HeadlessUiClientSteps(ScenarioWorkspace workspace)
    {
        myScenario = new BackendScenario(workspace);
        myScenario.EnableFakeProviderControl();
    }

    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

    [Given("a git project prepared with a {string} role using the fake provider fixture for the ui client")]
    public void GivenAGitProjectPreparedWithARoleUsingTheFakeProviderFixtureForTheUiClient(string role) =>
        myScenario.ConfigureRole(role);

    [Given("the published squad-hq is launched with \"--ui stdio\"")]
    public void GivenThePublishedSquadHqIsLaunchedWithUiStdio() =>
        myScenario.LaunchWithoutReadyHandshake<FakeAgentProviderFactory>();

    [Given("the ui client has completed the ready handshake")]
    [When("the ui client completes the ready handshake")]
    public void WhenTheUiClientCompletesTheReadyHandshake()
    {
        Await(myScenario.CompleteReadyHandshakeAsync());
        // Every session in this feature answers its own prompts automatically ("echo: {prompt}") across the
        // shared fake-provider control pipe instead of a second, narrower provider fixture - arming it here, once
        // the role's session has genuinely started, keeps every later prompt step semantic (prompt in, transcript
        // content out) with no per-prompt reply step of its own.
        Await(myScenario.WaitForRoleSessionStartedAsync("coder"));
        Await(myScenario.Agent("coder").EnableAutoEchoAsync());
    }

    [When("the ui client sends prompt {string} to role {string}")]
    public void WhenTheUiClientSendsPromptToRole(string prompt, string role) =>
        myScenario.SendPrompt(role, prompt);

    [Then("the ui client observes role {string} at status {string}")]
    public void ThenTheUiClientObservesRoleAtStatus(string role, string status) =>
        Await(myScenario.WaitForRoleStatusAsync(role, status));

    [Then("the ui client observes transcript content {string} for role {string}")]
    public void ThenTheUiClientObservesTranscriptContentForRole(string content, string role) =>
        Await(myScenario.WaitForTranscriptAsync(role, content));

    [Then("the ui client reports a protocol error")]
    public void ThenTheUiClientReportsAProtocolError() =>
        Await(myScenario.WaitForProtocolErrorAsync());

    [Then("waiting up to {int} second for role {string} to report status {string} times out with captured output")]
    public void ThenWaitingUpToSecondForRoleToReportStatusTimesOutWithCapturedOutput(int seconds, string role, string status)
    {
        var exception = Assert.ThrowsAsync<HeadlessUiWaitTimeoutException>(
            () => myScenario.WaitForRoleStatusAsync(role, status, TimeSpan.FromSeconds(seconds)));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain($"role '{role}' to report status '{status}'"));
            Assert.That(exception.Message, Does.Contain("Process:"));
            Assert.That(exception.Message, Does.Contain("State: running"));
            Assert.That(exception.Message, Does.Contain("Last known UI state:"));
            Assert.That(exception.Message, Does.Contain("StdOut:"));
            Assert.That(exception.Message, Does.Contain("StdErr:"));
        });
    }

    private static void Await(Task task) => task.GetAwaiter().GetResult();
}
