using squad.Specs.Support;
using squad.Specs.Support.Agents;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ProviderSelectionSteps
{
    private readonly ScenarioWorkspace myWorkspace;

    public ProviderSelectionSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [When("the operator launches Headquarters with a provider assembly that does not exist")]
    public void WhenTheOperatorLaunchesHeadquartersWithAMissingProviderAssembly() =>
        Launch($"{myWorkspace.PathInWorkspace("does-not-exist.dll")};Whatever.Type");

    [When("the operator launches Headquarters with an incompatible provider type")]
    public void WhenTheOperatorLaunchesHeadquartersWithAnIncompatibleProviderType() =>
        Launch(Descriptor(typeof(IncompatibleProviderFixture)));

    [When("the operator launches Headquarters with the provider option specified twice")]
    public void WhenTheOperatorLaunchesHeadquartersWithTheProviderOptionSpecifiedTwice() =>
        myWorkspace.RunTool(
            "squad-hq",
            ["launch", "--provider", Descriptor(typeof(ValidProviderFixtureFactory)), "--provider", Descriptor(typeof(ValidProviderFixtureFactory)), myWorkspace.Root]);

    [When("the operator launches Headquarters with a provider whose constructor throws")]
    public void WhenTheOperatorLaunchesHeadquartersWithAThrowingProviderConstructor() =>
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
