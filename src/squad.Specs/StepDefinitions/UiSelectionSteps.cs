using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class UiSelectionSteps
{
    private readonly ScenarioWorkspace myWorkspace;

    public UiSelectionSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [When("the executable launches with the ui option specified twice")]
    public void WhenTheExecutableLaunchesWithTheUiOptionSpecifiedTwice() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--ui", "stdio", "--ui", "stdio", myWorkspace.Root]);

    [When("the executable launches with the ui option missing its value")]
    public void WhenTheExecutableLaunchesWithTheUiOptionMissingItsValue() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--ui"]);

    [When("the executable launches with an unknown ui value")]
    public void WhenTheExecutableLaunchesWithAnUnknownUiValue() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--ui", "windows", myWorkspace.Root]);

    [Then("the launch fails with a UI diagnostic containing {string}")]
    public void ThenTheLaunchFailsWithAUiDiagnosticContaining(string expectedText)
    {
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain(expectedText));
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Not.Contain("Unhandled exception"));
    }
}
