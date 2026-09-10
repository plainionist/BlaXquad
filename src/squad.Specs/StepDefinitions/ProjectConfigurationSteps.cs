using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Configuration-author language for describing a squad's roles as they are declared in `blaxquad/squad.json`.
/// Requests the scenario's single <see cref="BackendScenario"/> instance rather than constructing its own, so
/// configuration performed here is visible to every other language module's steps within the same scenario.
/// </summary>
[Binding]
public sealed class ProjectConfigurationSteps
{
    private readonly BackendScenario myScenario;

    public ProjectConfigurationSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [Given("`blaxquad\\/squad.json` configures:")]
    public void GivenBlaxquadSquadJsonConfigures(Table roles) =>
        myScenario.ConfigureRoles(roles.Rows.Select(row => row["role"]).ToArray());
}
