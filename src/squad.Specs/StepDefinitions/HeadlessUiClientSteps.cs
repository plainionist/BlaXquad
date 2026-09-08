using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the real, separately launched, published squad-hq "--ui stdio" process through the reusable
/// <see cref="HeadlessUiClient"/>, proving the client's semantic operations against a real published headquarters
/// process. No step here parses protocol envelopes, touches raw JSON, reads a stream, or otherwise manages the
/// child process directly.
/// </summary>
[Binding]
public sealed class HeadlessUiClientSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private HeadlessUiClient? myClient;

    public HeadlessUiClientSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("the published squad-hq is launched with \"--ui stdio\"")]
    public void GivenThePublishedSquadHqIsLaunchedWithUiStdio() =>
        myClient = myWorkspace.StartHeadlessUiClient<EchoAgentProviderFactory>();

    [Given("the ui client has completed the ready handshake")]
    [When("the ui client completes the ready handshake")]
    public void WhenTheUiClientCompletesTheReadyHandshake() =>
        Await(myClient!.CompleteReadyHandshakeAsync());

    [When("the ui client sends prompt {string} to role {string}")]
    public void WhenTheUiClientSendsPromptToRole(string prompt, string role) =>
        myClient!.SendPrompt(role, prompt);

    [Then("the ui client observes role {string} at status {string}")]
    public void ThenTheUiClientObservesRoleAtStatus(string role, string status) =>
        Await(myClient!.WaitForRoleStatusAsync(role, status));

    [Then("the ui client observes transcript content {string} for role {string}")]
    public void ThenTheUiClientObservesTranscriptContentForRole(string content, string role) =>
        Await(myClient!.WaitForTranscriptAsync(role, content));

    [Then("the ui client reports a protocol error")]
    public void ThenTheUiClientReportsAProtocolError() =>
        Await(myClient!.WaitForProtocolErrorAsync());

    [Then("waiting up to {int} second for role {string} to report status {string} times out with captured output")]
    public void ThenWaitingUpToSecondForRoleToReportStatusTimesOutWithCapturedOutput(int seconds, string role, string status)
    {
        var exception = Assert.ThrowsAsync<HeadlessUiWaitTimeoutException>(
            () => myClient!.WaitForRoleStatusAsync(role, status, TimeSpan.FromSeconds(seconds)));
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
