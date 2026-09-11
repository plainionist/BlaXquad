using squad.Specs.Support.Scenarios;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// The two role-scoped prompt-observation assertions that have no equivalent in the shared agent-session
/// vocabulary owned by <see cref="BackendScenarioSteps"/>: proving a role's agent has observed no prompt at all,
/// and proving it has not advanced past an earlier prompt while a later one is serialized behind it. Every other
/// step used by the prompt isolation, serialization, and readiness scenarios reuses the shared project
/// configuration, Headquarters lifecycle, and agent-session bindings directly. Requests the scenario's single
/// <see cref="BackendScenario"/> instance rather than constructing its own, so it observes the same running
/// process those shared bindings drive.
/// </summary>
[Binding]
public sealed class PromptIsolationAndReadinessSteps
{
    private readonly BackendScenario myScenario;

    public PromptIsolationAndReadinessSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [Then("the {string} agent has observed no prompt")]
    public void ThenTheAgentHasObservedNoPrompt(string role) => Assert.That(myScenario.Agent(role).LatestPrompt(), Is.Null);

    [Then("the {string} agent has only observed the prompt {string}")]
    public void ThenTheAgentHasOnlyObservedThePrompt(string role, string expectedPrompt) =>
        Assert.That(myScenario.Agent(role).LatestPrompt(), Is.EqualTo(expectedPrompt));
}
