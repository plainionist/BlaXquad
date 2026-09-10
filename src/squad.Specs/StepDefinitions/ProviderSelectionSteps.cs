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

    [When("the executable launches with a provider assembly that does not exist")]
    public void WhenTheExecutableLaunchesWithAMissingProviderAssembly() =>
        Launch($"{myWorkspace.PathInWorkspace("does-not-exist.dll")};Whatever.Type");

    [When("the executable launches with an incompatible provider type")]
    public void WhenTheExecutableLaunchesWithAnIncompatibleProviderType() =>
        Launch(Descriptor(typeof(IncompatibleProviderFixture)));

    [When("the executable launches with the provider option specified twice")]
    public void WhenTheExecutableLaunchesWithTheProviderOptionSpecifiedTwice() =>
        myWorkspace.RunTool(
            "squad-hq",
            ["launch", "--provider", Descriptor(typeof(ValidProviderFixtureFactory)), "--provider", Descriptor(typeof(ValidProviderFixtureFactory)), myWorkspace.Root]);

    [When("the executable launches with a provider whose constructor throws")]
    public void WhenTheExecutableLaunchesWithAThrowingProviderConstructor() =>
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
