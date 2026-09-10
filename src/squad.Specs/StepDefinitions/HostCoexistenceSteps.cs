using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Launches two independent squad-hq stdio processes, each against its own <see cref="ScenarioWorkspace"/> and
/// <see cref="BackendScenario"/> pair, to prove that stopping or terminating one instance never disturbs the
/// other. Drives every process exclusively through the shared <see cref="BackendScenario"/> process driver and the
/// shared <see cref="FakeAgentProviderFactory"/> fixture - never touching workspace paths, process handles,
/// protocol DTOs, or raw JSON directly; each project's "coder" role answers its own prompts automatically
/// ("echo: {prompt}") across the shared fake-provider control pipe instead of a second, narrower provider fixture.
/// </summary>
[Binding]
public sealed class HostCoexistenceSteps
{
    private readonly Dictionary<string, ScenarioWorkspace> myWorkspacesByLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BackendScenario> myScenariosByLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> myExitCodesByLabel = new(StringComparer.Ordinal);

    [Given("independent squad projects {string} and {string} using the fake provider fixture")]
    public void GivenIndependentSquadProjectsUsingTheFakeProviderFixture(string firstLabel, string secondLabel)
    {
        CreateProject(firstLabel);
        CreateProject(secondLabel);
    }

    [When("squad-hq is launched with \"--ui stdio\" for project {string}")]
    public void WhenSquadHqIsLaunchedWithUiStdioForProject(string label) =>
        myScenariosByLabel[label].LaunchWithoutReadyHandshake<FakeAgentProviderFactory>();

    [When("the ui sends \"ui.ready\" to project {string}")]
    public void WhenTheUiSendsUiReadyToProject(string label)
    {
        var scenario = myScenariosByLabel[label];
        Await(scenario.CompleteReadyHandshakeAsync());
        // This project's "coder" session answers its own prompts automatically ("echo: {prompt}") across the
        // shared fake-provider control pipe instead of a second, narrower provider fixture - arming it here, once
        // the role's session has genuinely started, keeps every later prompt step semantic with no per-prompt
        // reply step of its own.
        Await(scenario.WaitForRoleSessionStartedAsync("coder"));
        Await(scenario.Agent("coder").EnableAutoEchoAsync());
    }

    [When("the ui sends a {string} command for role {string} with prompt {string} to project {string}")]
    public void WhenTheUiSendsACommandForRoleWithPromptToProject(string type, string role, string prompt, string label)
    {
        if (type != "prompt.send")
        {
            throw new NotSupportedException($"Only the 'prompt.send' command is supported here, not '{type}'.");
        }
        myScenariosByLabel[label].SendPrompt(role, prompt);
    }

    [Then("a \"transcript.update\" message for role {string} with content {string} is written to stdout for project {string}")]
    public void ThenATranscriptUpdateMessageForRoleWithContentIsWrittenToStdoutForProject(string role, string content, string label) =>
        Await(myScenariosByLabel[label].WaitForTranscriptAsync(role, content));

    [When("squad-hq requests shutdown for project {string}")]
    public void WhenSquadHqRequestsShutdownForProject(string label) =>
        myExitCodesByLabel[label] = Await(myScenariosByLabel[label].ShutdownAsync());

    [Then("the shutdown request succeeds for project {string}")]
    public void ThenTheShutdownRequestSucceedsForProject(string label) =>
        Assert.That(myExitCodesByLabel[label], Is.Zero);

    [Then("the squad-hq process for project {string} exits with code {string}")]
    public void ThenTheSquadHqProcessForProjectExitsWithCode(string label, string expectedExitCode) =>
        Assert.That(myExitCodesByLabel[label], Is.EqualTo(int.Parse(expectedExitCode)));

    [Then("the squad-hq process for project {string} is still running")]
    public void ThenTheSquadHqProcessForProjectIsStillRunning(string label)
    {
        Thread.Sleep(200);
        Assert.That(myScenariosByLabel[label].IsRunning, Is.True, $"Project '{label}' should still be running.");
    }

    [AfterScenario]
    public void CleanUp()
    {
        foreach (var scenario in myScenariosByLabel.Values)
        {
            scenario.Dispose();
        }
        foreach (var workspace in myWorkspacesByLabel.Values)
        {
            workspace.Dispose();
        }
    }

    private void CreateProject(string label)
    {
        var workspace = new ScenarioWorkspace();
        myWorkspacesByLabel[label] = workspace;

        var scenario = new BackendScenario(workspace);
        scenario.ConfigureRole("coder");
        scenario.EnableFakeProviderControl();
        myScenariosByLabel[label] = scenario;
    }

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
