using System.Runtime.InteropServices;
using squad.Specs.Support.Scenarios;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HostingPackagingSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private string? myOptOutPublishDirectory;

    public HostingPackagingSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Then("the published squad-hq assembly has no compile-time dependency on a concrete hosting assembly")]
    public void ThenNoCompileTimeDependencyOnAConcreteHostingAssembly()
    {
        var depsJsonPath = Path.Combine(myWorkspace.PublishedToolsDirectory, "squad-hq.deps.json");
        Assert.That(File.Exists(depsJsonPath), Is.True, $"Expected published dependency manifest at '{depsJsonPath}'.");
        var depsJson = File.ReadAllText(depsJsonPath);
        Assert.That(depsJson, Does.Not.Contain("squad.Hosting.Photino"));
        Assert.That(depsJson, Does.Not.Contain("squad.Hosting.Stdio"));

        var csprojPath = Path.Combine(myWorkspace.RepositoryRootPath, "src", "squad-hq", "squad-hq.csproj");
        var csproj = File.ReadAllText(csprojPath);
        Assert.That(csproj, Does.Not.Contain("squad.Hosting.Photino.csproj"));
        Assert.That(csproj, Does.Not.Contain("squad.Hosting.Stdio.csproj"));
    }

    [Then("the published squad-hq output contains the Photino hosting assembly and its dependency manifest")]
    public void ThenPublishedOutputContainsThePhotinoHostingAssemblyAndItsDependencyManifest()
    {
        var toolsDir = myWorkspace.PublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(toolsDir, "squad.Hosting.Photino.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(toolsDir, "squad.Hosting.Photino.deps.json")), Is.True);
    }

    [Then("the published squad-hq output contains the Photino native runtime assets")]
    public void ThenPublishedOutputContainsThePhotinoNativeRuntimeAssets()
    {
        var toolsDir = myWorkspace.PublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(toolsDir, PhotinoNativeLibraryFileName)), Is.True);
        Assert.That(File.Exists(Path.Combine(toolsDir, "Photino.NET.dll")), Is.True);
    }

    [Then("the published squad-hq output contains the Vue dashboard and window icon")]
    public void ThenPublishedOutputContainsTheVueDashboardAndWindowIcon()
    {
        var toolsDir = myWorkspace.PublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(toolsDir, "ui", "index.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(toolsDir, "Assets", "BlaXquad.ico")), Is.True);
    }

    [Then("the published squad-hq output contains no stdio hosting plug-in")]
    public void ThenPublishedOutputContainsNoStdioHostingPlugin()
    {
        var toolsDir = myWorkspace.PublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(toolsDir, "squad.Hosting.Stdio.dll")), Is.False);
    }

    [When("squad-hq is published with the Photino hosting plug-in opted out")]
    public void WhenSquadHqIsPublishedWithThePhotinoHostingPluginOptedOut()
    {
        myOptOutPublishDirectory = myWorkspace.PathInWorkspace("hosting-opt-out-publish") + Path.DirectorySeparatorChar;
        var csprojPath = Path.Combine(myWorkspace.RepositoryRootPath, "src", "squad-hq", "squad-hq.csproj");
        var result = myWorkspace.Run(
            "dotnet",
            [
                "publish",
                csprojPath,
                "--configuration", "Release",
                "--runtime", RuntimeInformation.RuntimeIdentifier,
                "--output", myOptOutPublishDirectory,
                "--property:IncludePhotinoHosting=false",
                "--nologo",
                "--verbosity", "quiet",
            ]);
        Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
    }

    [Then("the opt-out publish output contains no Photino hosting assembly, runtime assets, or UI")]
    public void ThenTheOptOutPublishOutputContainsNoPhotinoAssets()
    {
        Assert.That(File.Exists(Path.Combine(myOptOutPublishDirectory!, "squad.Hosting.Photino.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(myOptOutPublishDirectory!, PhotinoNativeLibraryFileName)), Is.False);
        Assert.That(Directory.Exists(Path.Combine(myOptOutPublishDirectory!, "ui")), Is.False);
    }

    [Then("the published backend-spec output contains no Photino hosting assembly, dependency, runtime asset, Vue distribution, or window asset")]
    public void ThenPublishedBackendSpecOutputContainsNoPhotinoAssets()
    {
        var backendSpecDir = myWorkspace.NeutralPublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(backendSpecDir, "squad.Hosting.Photino.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(backendSpecDir, "squad.Hosting.Photino.deps.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(backendSpecDir, "Photino.NET.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(backendSpecDir, PhotinoNativeLibraryFileName)), Is.False);
        Assert.That(Directory.Exists(Path.Combine(backendSpecDir, "ui")), Is.False);
        Assert.That(File.Exists(Path.Combine(backendSpecDir, "Assets", "BlaXquad.ico")), Is.False);

        var depsJsonPath = Path.Combine(backendSpecDir, "squad-hq.deps.json");
        Assert.That(File.Exists(depsJsonPath), Is.True, $"Expected published dependency manifest at '{depsJsonPath}'.");
        Assert.That(File.ReadAllText(depsJsonPath), Does.Not.Contain("Photino"));
    }

    [Then("the published backend-spec output contains no stdio hosting plug-in")]
    public void ThenPublishedBackendSpecOutputContainsNoStdioHostingPlugin()
    {
        var backendSpecDir = myWorkspace.NeutralPublishedToolsDirectory;
        Assert.That(File.Exists(Path.Combine(backendSpecDir, "squad.Hosting.Stdio.dll")), Is.False);
    }

    // Photino's own publish flattens its native library directly into the output root under a platform-specific
    // file name rather than nesting it under "runtimes/<rid>/native/" the way the Copilot provider's native assets
    // are nested, so assertions must name the file this way to remain correct on a linux-x64 or osx publish.
    private static string PhotinoNativeLibraryFileName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Photino.Native.dll"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Photino.Native.dylib"
        : "Photino.Native.so";
}
