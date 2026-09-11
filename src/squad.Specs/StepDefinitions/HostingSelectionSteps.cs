using squad.AgentProvider.Fake;
using squad.Hosting.Fake;
using squad.Hosting.Stdio;
using squad.Specs.Support.Scenarios;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HostingSelectionSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private readonly BackendScenario myScenario;

    public HostingSelectionSteps(ScenarioWorkspace workspace, BackendScenario scenario)
    {
        myWorkspace = workspace;
        myScenario = scenario;
    }

    [When("the operator launches Headquarters with the hosting option specified twice")]
    public void WhenTheOperatorLaunchesHeadquartersWithTheHostingOptionSpecifiedTwice() =>
        myWorkspace.RunTool(
            "squad-hq",
            ["launch", "--hosting", Descriptor(typeof(StdioHostingFactory)), "--hosting", Descriptor(typeof(StdioHostingFactory)), myWorkspace.Root]);

    [When("the operator launches Headquarters with the hosting option missing its value")]
    public void WhenTheOperatorLaunchesHeadquartersWithTheHostingOptionMissingItsValue() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--hosting"]);

    [When("the operator launches Headquarters with a malformed hosting descriptor")]
    public void WhenTheOperatorLaunchesHeadquartersWithAMalformedHostingDescriptor() =>
        Launch("no-separator-in-this-value");

    [When("the operator launches Headquarters with a hosting assembly that does not exist")]
    public void WhenTheOperatorLaunchesHeadquartersWithAMissingHostingAssembly() =>
        Launch($"{myWorkspace.PathInWorkspace("does-not-exist.dll")};Whatever.Type");

    [When("the operator launches Headquarters with a hosting assembly that exists but cannot be loaded")]
    public void WhenTheOperatorLaunchesHeadquartersWithAnUnloadableHostingAssembly()
    {
        var path = myWorkspace.PathInWorkspace("not-a-managed-assembly.dll");
        File.WriteAllText(path, "This file exists but is not a managed assembly, so loading it must fail.");
        Launch($"{path};Whatever.Type");
    }

    [When("the operator launches Headquarters with an incompatible hosting type")]
    public void WhenTheOperatorLaunchesHeadquartersWithAnIncompatibleHostingType() =>
        Launch(Descriptor(typeof(IncompatibleHostingFixture)));

    [When("the operator launches Headquarters with a hosting type that has no public parameterless constructor")]
    public void WhenTheOperatorLaunchesHeadquartersWithAHostingTypeWithNoPublicParameterlessConstructor() =>
        Launch(Descriptor(typeof(NoParameterlessConstructorHostingFixtureFactory)));

    [When("the operator launches Headquarters with a hosting type whose constructor throws")]
    public void WhenTheOperatorLaunchesHeadquartersWithAThrowingHostingConstructor() =>
        Launch(Descriptor(typeof(ThrowingHostingFixtureFactory)));

    [Then("the launch fails with a hosting diagnostic containing {string}")]
    public void ThenTheLaunchFailsWithAHostingDiagnosticContaining(string expectedText)
    {
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Contain(expectedText));
        Assert.That(myWorkspace.LastResult?.StdErr, Does.Not.Contain("Unhandled exception"));
    }

    [When("the operator launches Headquarters with an explicit valid stdio hosting descriptor")]
    public void WhenTheOperatorLaunchesHeadquartersWithAnExplicitValidStdioHostingDescriptor()
    {
        myScenario.ConfigureRole("coder");
        Await(myScenario.StartAsync<FakeAgentProviderFactory>());
    }

    [When("the operator launches Headquarters with a hosting plug-in deployed alongside duplicate contract assemblies")]
    public void WhenTheOperatorLaunchesHeadquartersWithAHostingPluginDeployedAlongsideDuplicateContractAssemblies()
    {
        var pluginPath = myWorkspace.HostingFixtureDeploymentWithDuplicateContractsPath;
        myScenario.UseHostingDescriptor($"{pluginPath};{typeof(StdioHostingFactory).FullName}");
        myScenario.ConfigureRole("coder");
        Await(myScenario.StartAsync<FakeAgentProviderFactory>());
    }

    [Then("Headquarters completes the ready handshake")]
    public void ThenHeadquartersCompletesTheReadyHandshake() =>
        Assert.That(myScenario.IsReady, Is.True);

    private void Launch(string hostingDescriptor) =>
        myWorkspace.RunTool("squad-hq", ["launch", "--hosting", hostingDescriptor, myWorkspace.Root]);

    private static string Descriptor(Type fixtureType) =>
        $"{fixtureType.Assembly.Location};{fixtureType.FullName}";

    private static void Await(Task task) => task.GetAwaiter().GetResult();
}
