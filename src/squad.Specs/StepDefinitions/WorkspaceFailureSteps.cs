using squad.Specs.Support.Scenarios;
using squad.AgentProvider.Fake;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Operator-facing language for arranging an invalid workspace or configuration (a missing or malformed project
/// configuration file, a missing constitution prompt, a missing required helper script, or an unsafe shared
/// worktree path) before a real squad-hq launch, proving each is reported as its own specific diagnostic rather
/// than a generic provider-startup failure. Requests the scenario's single <see cref="BackendScenario"/> instance
/// rather than constructing its own, so the corruption performed here is visible to the launch and assertion
/// steps bound elsewhere (see <see cref="HeadquartersLifecycleSteps"/>) within the same scenario.
/// </summary>
[Binding]
public sealed class WorkspaceFailureSteps
{
    private readonly BackendScenario myScenario;

    public WorkspaceFailureSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [Given("the project configuration file is missing")]
    public void GivenTheProjectConfigurationFileIsMissing() => myScenario.RemoveProjectConfiguration();

    [Given("the project configuration file is malformed")]
    public void GivenTheProjectConfigurationFileIsMalformed() => myScenario.CorruptProjectConfiguration();

    [When("the project configuration file is restored")]
    public void WhenTheProjectConfigurationFileIsRestored() => myScenario.RestoreProjectConfiguration();

    [Given("the constitution prompt file is missing")]
    public void GivenTheConstitutionPromptFileIsMissing() => myScenario.RemoveConstitutionPrompt();

    [Given("role {string}'s worktree already has non-empty directory {string} configured as a shared worktree path")]
    public void GivenRoleSWorktreeAlreadyHasNonEmptyDirectoryConfiguredAsASharedWorktreePath(string role, string sharedPath) =>
        myScenario.ConfigureSharedWorktreePathWithExistingContent(sharedPath, role);

    [When("the operator launches Headquarters from a deployment missing its required helper script")]
    public void WhenTheOperatorLaunchesHeadquartersFromADeploymentMissingItsRequiredHelperScript() =>
        myScenario.LaunchDeploymentMissingHelperScriptWithoutReadyHandshake<FakeAgentProviderFactory>();
}
