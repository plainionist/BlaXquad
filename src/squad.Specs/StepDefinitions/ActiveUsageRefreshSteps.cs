using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the real, separately launched "squad-hq --ui stdio" process exclusively through <see
/// cref="BackendScenario"/> and the shared <see cref="FakeAgentProviderFactory"/> fixture, never touching workspace
/// paths, process handles, protocol DTOs, or product objects directly. Proves the process/protocol boundary
/// contract for the Copilot adapter's active usage refresh policy: provider-reported context and AIC usage received
/// while a role is still working reaches the real UI JSON protocol before idle, and the final snapshot after idle
/// preserves the latest reported values.
/// </summary>
[Binding]
public sealed class ActiveUsageRefreshSteps
{
    private readonly BackendScenario myScenario;

    public ActiveUsageRefreshSteps(ScenarioWorkspace workspace)
    {
        myScenario = new BackendScenario(workspace);
        myScenario.EnableFakeProviderControl();
    }

    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

    [Given("a git project configured with a {string} role using the fake provider fixture")]
    public void GivenAGitProjectConfiguredWithARoleUsingTheFakeProviderFixture(string role) =>
        myScenario.ConfigureRole(role);

    [When("squad-hq is launched with \"--ui stdio\" using the fake provider fixture")]
    public void WhenSquadHqIsLaunchedWithUiStdioUsingTheFakeProviderFixture() =>
        Await(myScenario.StartAsync<FakeAgentProviderFactory>());

    [When("the fake provider session for role {string} has started")]
    public void WhenTheFakeProviderSessionForRoleHasStarted(string role) =>
        Await(myScenario.WaitForRoleSessionStartedAsync(role));

    [When("the ui sends prompt {string} to role {string}")]
    public void WhenTheUiSendsPromptToRole(string prompt, string role)
    {
        myScenario.SendPrompt(role, prompt);
        // Waits for the fake session to have observably received this exact prompt (and therefore already
        // published the real "still working" AgentUserMessageEvent) before any usage is reported, so a following
        // usage report can never race the prompt's own arrival at the launched process.
        Await(myScenario.Agent(role).WaitForPromptAsync(observed => observed == prompt));
    }

    [When("the fake provider reports context usage {int} of {int} and AIC usage {decimal} for role {string}")]
    public void WhenTheFakeProviderReportsUsageForRole(int contextUsed, int contextLimit, decimal aicUsed, string role) =>
        Await(myScenario.Agent(role).ReportUsageAsync(contextUsed, contextLimit, aicUsed));

    [When("the fake provider goes idle for role {string} with context usage {int} of {int} and AIC usage {decimal}")]
    public void WhenTheFakeProviderGoesIdleForRole(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        Await(myScenario.Agent(role).CompleteWithIdleUsageAsync(contextUsed, contextLimit, aicUsed));

    [Then("a \"state.snapshot\" message reports role {string} as working with context usage {int} of {int} and AIC usage {decimal}")]
    public void ThenAStateSnapshotMessageReportsRoleAsWorkingWithUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        Await(myScenario.WaitForRoleUsageSnapshotAsync(role, isWorking: true, contextUsed, contextLimit, aicUsed));

    [Then("a \"state.snapshot\" message reports role {string} as idle with context usage {int} of {int} and AIC usage {decimal}")]
    public void ThenAStateSnapshotMessageReportsRoleAsIdleWithUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        Await(myScenario.WaitForRoleUsageSnapshotAsync(role, isWorking: false, contextUsed, contextLimit, aicUsed));

    [Then("no \"state.snapshot\" message reports role {string} with AIC usage {decimal} within {int} seconds")]
    public void ThenNoStateSnapshotMessageReportsRoleWithAicUsageWithinSeconds(string role, decimal aicUsed, int seconds) =>
        Assert.CatchAsync<TimeoutException>(() => myScenario.WaitForRoleUsageAsync(role, aicUsed, TimeSpan.FromSeconds(seconds)));

    private static void Await(Task task) => task.GetAwaiter().GetResult();
}
