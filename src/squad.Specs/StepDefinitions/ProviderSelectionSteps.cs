using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ProviderSelectionSteps
{
    private readonly ScenarioWorkspace myWorkspace;

    public ProviderSelectionSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [When("squad-hq launches with a provider assembly that does not exist")]
    public void WhenSquadHqLaunchesWithAMissingProviderAssembly() =>
        Launch($"{myWorkspace.PathInWorkspace("does-not-exist.dll")};Whatever.Type");

    [When("squad-hq launches with an incompatible provider type")]
    public void WhenSquadHqLaunchesWithAnIncompatibleProviderType() =>
        Launch(Descriptor(typeof(IncompatibleProviderFixture)));

    [When("squad-hq launches with the provider option specified twice")]
    public void WhenSquadHqLaunchesWithTheProviderOptionSpecifiedTwice() =>
        myWorkspace.RunTool(
            "squad-hq",
            ["launch", "--provider", Descriptor(typeof(ValidProviderFixtureFactory)), "--provider", Descriptor(typeof(ValidProviderFixtureFactory)), myWorkspace.Root]);

    [When("squad-hq launches with a provider whose constructor throws")]
    public void WhenSquadHqLaunchesWithAThrowingProviderConstructor() =>
        Launch(Descriptor(typeof(ThrowingProviderFixtureFactory)));

    [Then("the launch fails with a provider diagnostic containing {string}")]
    public void ThenTheLaunchFailsWithAProviderDiagnosticContaining(string expectedText)
    {
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain(expectedText));
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Not.Contain("Unhandled exception"));
    }

    private void Launch(string providerDescriptor) =>
        myWorkspace.RunTool("squad-hq", ["launch", "--provider", providerDescriptor, myWorkspace.Root]);

    private static string Descriptor(Type fixtureType) =>
        $"{typeof(ProviderSelectionSteps).Assembly.Location};{fixtureType.FullName}";
}
