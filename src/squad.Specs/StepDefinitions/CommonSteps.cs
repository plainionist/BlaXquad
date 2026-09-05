using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class CommonSteps
{
    private readonly ScenarioWorkspace myWorkspace;

    public CommonSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Then("the command succeeds")]
    public void ThenTheCommandSucceeds()
    {
        Assert.That(myWorkspace.LastResult, Is.Not.Null);
        Assert.That(myWorkspace.LastResult!.ExitCode, Is.Zero,
            () => $"stderr:{Environment.NewLine}{myWorkspace.LastResult.StdErr}");
    }

    [Then("the command fails")]
    public void ThenTheCommandFails()
    {
        Assert.That(myWorkspace.LastResult, Is.Not.Null);
        Assert.That(myWorkspace.LastResult!.ExitCode, Is.Not.Zero);
    }

    [Then("the command exits with code {int}")]
    public void ThenTheCommandExitsWithCode(int exitCode)
    {
        Assert.That(myWorkspace.LastResult, Is.Not.Null);
        Assert.That(myWorkspace.LastResult!.ExitCode, Is.EqualTo(exitCode));
    }

    [Then("standard output contains {string}")]
    public void ThenStandardOutputContains(string expected)
    {
        Assert.That(myWorkspace.LastResult, Is.Not.Null);
        Assert.That(myWorkspace.LastResult!.StdOut, Does.Contain(expected));
    }

    [Then("standard error contains {string}")]
    public void ThenStandardErrorContains(string expected)
    {
        Assert.That(myWorkspace.LastResult, Is.Not.Null);
        Assert.That(myWorkspace.LastResult!.StdErr, Does.Contain(expected));
    }

}



