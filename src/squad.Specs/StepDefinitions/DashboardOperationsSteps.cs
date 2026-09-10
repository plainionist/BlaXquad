using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Dashboard-user language for sending a role-directed prompt and observing the resulting transcript content -
/// the "Dashboard operations" canonical vocabulary module: a user sends prompts and the dashboard shows transcript
/// content. Requests the scenario's single <see cref="BackendScenario"/> instance rather than constructing its
/// own, so it observes the same running process other language modules' steps drive. Sends the prompt and awaits
/// the real production transcript projection through that shared facade - never a test-owned actor name or the
/// fake-agent control pipe - so this vocabulary is safe to reuse from any future migrated feature needing the same
/// dashboard behavior.
/// </summary>
[Binding]
public sealed class DashboardOperationsSteps
{
    private readonly BackendScenario myScenario;

    public DashboardOperationsSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [When("the user sends {string} to role {string}")]
    public void WhenTheUserSendsToRole(string prompt, string role) => myScenario.SendPrompt(role, prompt);

    [Then("the transcript for role {string} contains {string}")]
    public async Task ThenTheTranscriptForRoleContains(string role, string content) =>
        await myScenario.WaitForTranscriptAsync(role, content);
}
