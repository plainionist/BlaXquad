namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class RuntimePackagingSteps
{
    [Then("the published squad-hq output contains the squad.Runtime assembly")]
    public void ThenPublishedOutputContainsTheSquadRuntimeAssembly()
    {
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "squad-tools");
        Assert.That(File.Exists(Path.Combine(toolsDir, "squad.Runtime.dll")), Is.True);
    }

    [Then("the published squad-hq output contains no squad.Host.Runtime or squad.Host.Control assembly")]
    public void ThenPublishedOutputContainsNoObsoleteHostAssembly()
    {
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "squad-tools");
        Assert.That(File.Exists(Path.Combine(toolsDir, "squad.Host.Runtime.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(toolsDir, "squad.Host.Control.dll")), Is.False);
    }
}
