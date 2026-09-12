namespace squad.Specs.Support.Scenarios;

/// <summary>Test-owned lifecycle for one independent project in a coexistence scenario - the
/// <see cref="squad.Specs.StepDefinitions.HostCoexistenceSteps"/> binding's own registry entry per project label,
/// replacing what used to be two separate label-keyed dictionaries. <see cref="Scenario"/> is required (a project
/// always owns exactly one shared scenario for its entire lifetime); <see cref="ExitCode"/> stays null until the
/// operator shuts the project down.</summary>
internal sealed class ProjectObservationState
{
    internal ProjectObservationState(BackendScenario scenario)
    {
        Scenario = scenario;
    }

    internal BackendScenario Scenario { get; }

    internal int? ExitCode { get; set; }
}
