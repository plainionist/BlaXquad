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
    private Exception? myLastWaitException;
    private string? myObservedHarnessMessage;

    public BackendScenarioSteps(ScenarioWorkspace workspace)
    {
        myScenario = new BackendScenario(workspace);
    }

    /// <summary>
    /// Disposes the scenario's <see cref="BackendScenario"/> after every scenario - not just the ones that reach a
    /// normal host-control shutdown. This is the teardown path that actually runs for every process-driver
    /// scenario (Reqnroll disposes the injected <see cref="ScenarioWorkspace"/> automatically, but never this
    /// manually constructed composition root), so it is the only place a leaked, never-disposed session can be
    /// reported.
    /// </summary>
    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

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

    [Then("the {string} agent observes a harness message")]
    public void ThenTheAgentObservesAHarnessMessage(string role) =>
        myObservedHarnessMessage = Await(myScenario.Agent(role).WaitForHarnessMessageAsync());

    [Then("the observed harness message appears in the transcript for role {string}")]
    public void ThenTheObservedHarnessMessageAppearsInTheTranscriptForRole(string role)
    {
        Assert.That(myObservedHarnessMessage, Is.Not.Null);
        Await(myScenario.WaitForTranscriptAsync(role, myObservedHarnessMessage!));
    }

    [When("the backend scenario requests an abort for role {string}")]
    public void WhenTheBackendScenarioRequestsAnAbortForRole(string role) => myScenario.RequestAbort(role);

    [Then("the {string} agent observes an abort")]
    public void ThenTheAgentObservesAnAbort(string role) => Await(myScenario.Agent(role).WaitForAbortAsync());

    [When("the {string} agent requests permission {string} with description {string}")]
    public void WhenTheAgentRequestsPermissionWithDescription(string role, string requestId, string description) =>
        Await(myScenario.Agent(role).RequestPermissionAsync(requestId, description));

    [When("the backend scenario responds to permission {string} for role {string} with approved {string}")]
    public void WhenTheBackendScenarioRespondsToPermissionForRoleWithApproved(string requestId, string role, string approved) =>
        myScenario.RespondToPermission(role, requestId, bool.Parse(approved));

    [Then("the {string} agent observes a permission response for {string} approved {string}")]
    public void ThenTheAgentObservesAPermissionResponseForApproved(string role, string requestId, string approved)
    {
        var response = Await(myScenario.Agent(role).WaitForPermissionResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Approved, Is.EqualTo(bool.Parse(approved)));
        });
    }

    [When("the {string} agent requests input {string} with prompt {string}")]
    public void WhenTheAgentRequestsInputWithPrompt(string role, string requestId, string prompt) =>
        Await(myScenario.Agent(role).RequestInputAsync(requestId, prompt));

    [When("the backend scenario responds to input {string} for role {string} with answer {string}")]
    public void WhenTheBackendScenarioRespondsToInputForRoleWithAnswer(string requestId, string role, string answer) =>
        myScenario.RespondToInput(role, requestId, answer);

    [Then("the {string} agent observes an input response for {string} with answer {string}")]
    public void ThenTheAgentObservesAnInputResponseForWithAnswer(string role, string requestId, string answer)
    {
        var response = Await(myScenario.Agent(role).WaitForInputResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Answer, Is.EqualTo(answer));
        });
    }

    [When("the {string} agent requests elicitation {string} with prompt {string} and mode {string}")]
    public void WhenTheAgentRequestsElicitationWithPromptAndMode(string role, string requestId, string prompt, string mode) =>
        Await(myScenario.Agent(role).RequestElicitationAsync(requestId, prompt, mode));

    [When("the backend scenario responds to elicitation {string} for role {string} with action {string}")]
    public void WhenTheBackendScenarioRespondsToElicitationForRoleWithAction(string requestId, string role, string action) =>
        myScenario.RespondToElicitation(role, requestId, action);

    [Then("the {string} agent observes an elicitation response for {string} with action {string}")]
    public void ThenTheAgentObservesAnElicitationResponseForWithAction(string role, string requestId, string action)
    {
        var response = Await(myScenario.Agent(role).WaitForElicitationResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Action, Is.EqualTo(action));
        });
    }

    [When("the {string} agent emits the reasoning {string}")]
    public void WhenTheAgentEmitsTheReasoning(string role, string content) =>
        Await(myScenario.Agent(role).EmitReasoningAsync(content));

    [When("the {string} agent emits a full tool lifecycle for tool call {string} named {string}")]
    public void WhenTheAgentEmitsAFullToolLifecycleForToolCallNamed(string role, string toolCallId, string toolName) =>
        Await(EmitFullToolLifecycleAsync(role, toolCallId, toolName));

    [When("the {string} agent emits idle")]
    public void WhenTheAgentEmitsIdle(string role) => Await(myScenario.Agent(role).EmitIdleAsync());

    [When("the {string} agent emits readiness {string}")]
    public void WhenTheAgentEmitsReadiness(string role, string state) => Await(myScenario.Agent(role).EmitReadinessAsync(state));

    [When("the {string} agent emits usage {string}")]
    public void WhenTheAgentEmitsUsage(string role, string aicUsed) =>
        Await(myScenario.Agent(role).EmitUsageAsync(decimal.Parse(aicUsed)));

    [Then("the backend scenario observes role {string} at AI-credit usage {string}")]
    public void ThenTheBackendScenarioObservesRoleAtAiCreditUsage(string role, string aicUsed) =>
        Await(myScenario.WaitForRoleUsageAsync(role, decimal.Parse(aicUsed)));

    [When("the {string} agent completes its session")]
    public void WhenTheAgentCompletesItsSession(string role) => Await(myScenario.Agent(role).CompleteSessionAsync());

    [When("the {string} agent fails its session with message {string}")]
    public void WhenTheAgentFailsItsSessionWithMessage(string role, string message) =>
        Await(myScenario.Agent(role).FailSessionAsync(message));

    private async Task EmitFullToolLifecycleAsync(string role, string toolCallId, string toolName)
    {
        var agent = myScenario.Agent(role);
        await agent.EmitToolStartedAsync(toolCallId, toolName);
        await agent.EmitToolProgressAsync(toolCallId, "Running...");
        await agent.EmitToolOutputChangedAsync(toolCallId, "partial output");
        await agent.EmitToolCompletedAsync(toolCallId, toolName, succeeded: true);
    }

    [When("the backend scenario requests a host-control shutdown")]
    public void WhenTheBackendScenarioRequestsAHostControlShutdown() =>
        myExitCode = Await(myScenario.ShutdownAsync());

    [Then("the backend scenario observes an exit code of zero")]
    public void ThenTheBackendScenarioObservesAnExitCodeOfZero() =>
        Assert.That(myExitCode, Is.Zero);

    [When("the backend scenario waits {int} seconds for role {string} at status {string}")]
    public void WhenTheBackendScenarioWaitsSecondsForRoleAtStatus(int seconds, string role, string status) =>
        Await(WaitAndCaptureAsync(role, status, TimeSpan.FromSeconds(seconds)));

    [Then("the wait fails with a diagnostics block naming the process, the UI protocol state, and the provider observations")]
    public void ThenTheWaitFailsWithCombinedDiagnostics()
    {
        Assert.That(myLastWaitException, Is.Not.Null);
        var message = myLastWaitException!.Message;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Process:"));
            Assert.That(message, Does.Contain("Last known UI state:"));
            Assert.That(message, Does.Contain("Observations:"));
        });
    }

    private async Task WaitAndCaptureAsync(string role, string status, TimeSpan timeout)
    {
        myLastWaitException = null;
        try
        {
            await myScenario.WaitForRoleStatusAsync(role, status, timeout);
        }
        catch (Exception exception)
        {
            myLastWaitException = exception;
        }
    }

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
