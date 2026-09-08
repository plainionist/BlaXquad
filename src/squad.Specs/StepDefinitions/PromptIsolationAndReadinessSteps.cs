using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the prompt isolation, serialization, and readiness scenarios exclusively through
/// <see cref="BackendScenario"/>: real "prompt.send" UI commands, the fake-agent control pipe, and real
/// "squad-hq wait-for-agent" processes. Never touches <c>SquadViewModel</c>, role dictionaries, pending-interaction
/// collections, or the operation coordinator directly.
/// </summary>
[Binding]
public sealed class PromptIsolationAndReadinessSteps
{
    private readonly BackendScenario myScenario;
    private readonly Dictionary<string, BackendScenarioCommand> myReadinessWaits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommandResult> myReadinessResults = new(StringComparer.Ordinal);

    public PromptIsolationAndReadinessSteps(ScenarioWorkspace workspace)
    {
        myScenario = new BackendScenario(workspace);
    }

    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

    [Given("a role-interaction scenario configured with roles {string}")]
    public void GivenARoleInteractionScenarioConfiguredWithRoles(string commaSeparatedRoles)
    {
        var roles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myScenario.ConfigureRoles(roles);
        myScenario.EnableFakeProviderControl();
    }

    [When("the role-interaction scenario starts with both roles established")]
    public async Task WhenTheRoleInteractionScenarioStartsWithBothRolesEstablished()
    {
        await myScenario.StartAsync<FakeAgentProviderFactory>();
        await myScenario.WaitForRoleSessionStartedAsync("coder");
        await myScenario.WaitForRoleSessionStartedAsync("reviewer");
    }

    [When("the role-interaction scenario starts without waiting for either role to establish")]
    public async Task WhenTheRoleInteractionScenarioStartsWithoutWaitingForEitherRoleToEstablish() =>
        await myScenario.StartAsync<FakeAgentProviderFactory>();

    [When("the {string} agent session establishes")]
    public async Task WhenTheAgentSessionEstablishes(string role) => await myScenario.WaitForRoleSessionStartedAsync(role);

    [When("the role-interaction scenario sends the prompt {string} to role {string}")]
    public void WhenTheRoleInteractionScenarioSendsThePromptToRole(string prompt, string role) =>
        myScenario.SendPrompt(role, prompt);

    [Then("the {string} agent has received the prompt {string}")]
    public async Task ThenTheAgentHasReceivedThePrompt(string role, string expectedPrompt) =>
        Assert.That(await myScenario.Agent(role).WaitForPromptAsync(), Is.EqualTo(expectedPrompt));

    [Then("the {string} agent has eventually received the prompt {string}")]
    public async Task ThenTheAgentHasEventuallyReceivedThePrompt(string role, string expectedPrompt) =>
        Assert.That(await myScenario.Agent(role).WaitForPromptAsync(prompt => prompt == expectedPrompt), Is.EqualTo(expectedPrompt));

    [Then("the {string} agent has observed no prompt")]
    public void ThenTheAgentHasObservedNoPrompt(string role) => Assert.That(myScenario.Agent(role).LatestPrompt(), Is.Null);

    [Then("the {string} agent has only observed the prompt {string}")]
    public void ThenTheAgentHasOnlyObservedThePrompt(string role, string expectedPrompt) =>
        Assert.That(myScenario.Agent(role).LatestPrompt(), Is.EqualTo(expectedPrompt));

    [When("the {string} agent answers with {string}")]
    public async Task WhenTheAgentAnswersWith(string role, string content) => await myScenario.Agent(role).ReplyAsync(content);

    [When("the {string} agent reports idle")]
    public async Task WhenTheAgentReportsIdle(string role) => await myScenario.Agent(role).EmitIdleAsync();

    [Then("the role-interaction scenario observes the transcript for role {string} containing {string}")]
    public async Task ThenTheRoleInteractionScenarioObservesTheTranscriptForRoleContaining(string role, string content) =>
        await myScenario.WaitForTranscriptAsync(role, content);

    [When("the role-interaction scenario begins waiting for the {string} agent to become ready")]
    public void WhenTheRoleInteractionScenarioBeginsWaitingForTheAgentToBecomeReady(string role) =>
        myReadinessWaits[role] = myScenario.StartWaitForAgent(role, TimeSpan.FromSeconds(10));

    [Then("the {string} readiness wait remains pending")]
    public async Task ThenTheReadinessWaitRemainsPending(string role)
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

    [Then("the {string} readiness wait succeeds")]
    public async Task ThenTheReadinessWaitSucceeds(string role)
    {
        var result = await myReadinessWaits[role].WaitForCompletionAsync(TimeSpan.FromSeconds(10));
        myReadinessResults[role] = result;
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
            Assert.That(result.StdOut, Does.Contain("is ready"));
        });
    }
}
