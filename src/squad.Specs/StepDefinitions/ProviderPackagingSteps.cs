using System.Runtime.InteropServices;
using squad.Specs.Support.Scenarios;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ProviderPackagingSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private string? myOptOutPublishDirectory;

    public ProviderPackagingSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Then("the published squad-hq assembly has no compile-time dependency on squad.AgentProvider.CopilotSdk")]
    public void ThenNoCompileTimeDependencyOnCopilotSdk()
    {
        var depsJsonPath = Path.Combine(myWorkspace.PublishedToolsDirectory, "squad-hq.deps.json");
        Assert.That(File.Exists(depsJsonPath), Is.True, $"Expected published dependency manifest at '{depsJsonPath}'.");
        Assert.That(File.ReadAllText(depsJsonPath), Does.Not.Contain("squad.AgentProvider.CopilotSdk"));

        var csprojPath = Path.Combine(myWorkspace.RepositoryRootPath, "src", "squad-hq", "squad-hq.csproj");
        Assert.That(File.ReadAllText(csprojPath), Does.Not.Contain("squad.AgentProvider.CopilotSdk.csproj"));
    }

    [Then("the published squad-hq output contains the Copilot provider assembly")]
    public void ThenPublishedOutputContainsTheCopilotProviderAssembly()
    {
        var toolsDir = myWorkspace.PublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(toolsDir, "squad.AgentProvider.CopilotSdk.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(toolsDir, "GitHub.Copilot.SDK.dll")), Is.True);
    }

    [Then("the published squad-hq output contains the Copilot native runtime assets")]
    public void ThenPublishedOutputContainsTheCopilotNativeRuntimeAssets()
    {
        var runtimesDir = Path.Combine(myWorkspace.PublishedToolsDirectory, "runtimes");
        Assert.That(Directory.Exists(runtimesDir), Is.True, $"Expected '{runtimesDir}' to exist.");
        Assert.That(Directory.GetDirectories(runtimesDir), Is.Not.Empty, $"Expected at least one runtime identifier folder under '{runtimesDir}'.");
    }

    [When("squad-hq is published with the Copilot provider opted out")]
    public void WhenSquadHqIsPublishedWithTheCopilotProviderOptedOut()
    {
        myOptOutPublishDirectory = myWorkspace.PathInWorkspace("opt-out-publish") + Path.DirectorySeparatorChar;
        var csprojPath = Path.Combine(myWorkspace.RepositoryRootPath, "src", "squad-hq", "squad-hq.csproj");
        var result = myWorkspace.Run(
            "dotnet",
            [
                "publish",
                csprojPath,
                "--configuration", "Release",
                "--runtime", RuntimeInformation.RuntimeIdentifier,
                "--output", myOptOutPublishDirectory,
                "--property:IncludeCopilotSdkProvider=false",
                "--nologo",
                "--verbosity", "quiet",
            ]);
        Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
    }

    [Then("the opt-out publish output contains no Copilot provider assembly or runtime assets")]
    public void ThenTheOptOutPublishOutputContainsNoCopilotAssets()
    {
        Assert.That(File.Exists(Path.Combine(myOptOutPublishDirectory!, "squad.AgentProvider.CopilotSdk.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(myOptOutPublishDirectory!, "GitHub.Copilot.SDK.dll")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(myOptOutPublishDirectory!, "runtimes")), Is.False);
    }

    [Then("the published backend-spec output contains the squad-hq executable")]
    public void ThenPublishedBackendSpecOutputContainsTheSquadHqExecutable()
    {
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "squad-hq.exe" : "squad-hq";
        var backendSpecDir = myWorkspace.NeutralPublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(backendSpecDir, executableName)), Is.True);
    }

    [Then("the published backend-spec output contains no Copilot provider assembly, dependency, or runtime asset")]
    public void ThenPublishedBackendSpecOutputContainsNoCopilotAssets()
    {
        var backendSpecDir = myWorkspace.NeutralPublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(backendSpecDir, "squad.AgentProvider.CopilotSdk.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(backendSpecDir, "GitHub.Copilot.SDK.dll")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(backendSpecDir, "runtimes")), Is.False);

        var depsJsonPath = Path.Combine(backendSpecDir, "squad-hq.deps.json");
        Assert.That(File.Exists(depsJsonPath), Is.True, $"Expected published dependency manifest at '{depsJsonPath}'.");
        Assert.That(File.ReadAllText(depsJsonPath), Does.Not.Contain("squad.AgentProvider.CopilotSdk"));
    }
}
