using squad.Specs.Support.Scenarios;
using squad.AgentProvider.Fake;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Launches two independent squad-hq stdio processes, each against its own <see cref="ScenarioWorkspace"/> and
/// <see cref="BackendScenario"/> pair, to prove that stopping or terminating one instance never disturbs the
/// other. Each independent project is created through the scenario's single shared <see cref="BackendScenario"/>
/// composition root (see <see cref="BackendScenario.CreateIndependentProject"/>), which tracks and disposes both
/// child scenarios and their own workspaces; this binding never constructs a <see cref="ScenarioWorkspace"/> or
/// <see cref="BackendScenario"/> directly and owns no teardown of its own. Drives every process exclusively
/// through the shared <see cref="BackendScenario"/> process driver and the shared
/// <see cref="FakeAgentProviderFactory"/> fixture - never touching workspace paths, process handles, protocol
/// DTOs, or raw JSON directly; each project's "coder" role answers its own prompts automatically
/// ("echo: {prompt}") across the shared fake-provider control pipe instead of a second, narrower provider fixture.
/// </summary>
[Binding]
public sealed class HostCoexistenceSteps
{
    private readonly BackendScenario myScenario;
    private readonly Dictionary<string, ProjectObservationState> myProjectsByLabel = new(StringComparer.Ordinal);

    public HostCoexistenceSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [Given("the operator configures independent squad projects {string} and {string}")]
    public void GivenTheOperatorConfiguresIndependentSquadProjects(string firstLabel, string secondLabel)
    {
        CreateProject(firstLabel);
        CreateProject(secondLabel);
    }

    [When("the operator launches Headquarters with the \"stdio\" UI transport for project {string}")]
    public void WhenTheOperatorLaunchesHeadquartersWithTheStdioUiTransportForProject(string label) =>
        myProjectsByLabel[label].Scenario.LaunchWithoutReadyHandshake<FakeAgentProviderFactory>();

    [When("a UI-protocol client sends \"ui.ready\" to project {string}")]
    public void WhenAUiProtocolClientSendsUiReadyToProject(string label)
    {
        var scenario = myProjectsByLabel[label].Scenario;
        Await(scenario.CompleteReadyHandshakeAsync());
        // This project's "coder" session answers its own prompts automatically ("echo: {prompt}") across the
        // shared fake-provider control pipe instead of a second, narrower provider fixture - arming it here, once
        // the role's session has genuinely started, keeps every later prompt step semantic with no per-prompt
        // reply step of its own.
        Await(scenario.WaitForRoleSessionStartedAsync("coder"));
        Await(scenario.Agent("coder").EnableAutoEchoAsync());
    }

    [When("a UI-protocol client sends a {string} command for role {string} with prompt {string} to project {string}")]
    public void WhenAUiProtocolClientSendsACommandForRoleWithPromptToProject(string type, string role, string prompt, string label)
    {
        if (type != "prompt.send")
        {
            throw new NotSupportedException($"Only the 'prompt.send' command is supported here, not '{type}'.");
        }
        myProjectsByLabel[label].Scenario.SendPrompt(role, prompt);
    }

    [Then("a \"transcript.update\" message for role {string} with content {string} is written to stdout for project {string}")]
    public void ThenATranscriptUpdateMessageForRoleWithContentIsWrittenToStdoutForProject(string role, string content, string label) =>
        Await(myProjectsByLabel[label].Scenario.WaitForTranscriptAsync(role, content));

    [When("the operator shuts down Headquarters for project {string}")]
    public void WhenTheOperatorShutsDownHeadquartersForProject(string label) =>
        myProjectsByLabel[label].ExitCode = Await(myProjectsByLabel[label].Scenario.ShutdownAsync());

    [Then("Headquarters exits with code {int} for project {string}")]
    public void ThenHeadquartersExitsWithCodeForProject(int expectedExitCode, string label) =>
        Assert.That(myProjectsByLabel[label].ExitCode, Is.EqualTo(expectedExitCode));

    [Then("Headquarters for project {string} is still running")]
    public void ThenHeadquartersForProjectIsStillRunning(string label)
    {
        Thread.Sleep(200);
        Assert.That(myProjectsByLabel[label].Scenario.IsRunning, Is.True, $"Project '{label}' should still be running.");
    }

    private void CreateProject(string label)
    {
        var scenario = myScenario.CreateIndependentProject();
        scenario.ConfigureRole("coder");
        scenario.EnableFakeProviderControl();
        myProjectsByLabel[label] = new ProjectObservationState(scenario);
    }

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
