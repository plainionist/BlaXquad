using global::squad.Specs.Support;
using global::squad.Agent;
using global::squad.Core;
using System.Reflection;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ArchitectureSteps
{
    private readonly ScenarioWorkspace myWorkspace;

    public ArchitectureSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Then("the shared core assembly has no technology package references")]
    public void ThenTheSharedCoreAssemblyHasNoTechnologyPackageReferences()
    {
        var references = typeof(SquadViewModel).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.That(references, Does.Not.Contain("GitHub.Copilot.SDK"));
        Assert.That(references, Does.Not.Contain("Photino.NET"));
    }

    [Then("technology implementation files are outside the core project")]
    public void ThenTechnologyImplementationFilesAreOutsideTheCoreProject()
    {
        var coreDirectory = Path.Combine(myWorkspace.RepositoryRootPath, "src", "squad.Core");
        var forbiddenFiles = new[]
        {
            "CopilotSdkBackend.cs",
            "CopilotSdkClient.cs",
            "CopilotSdkAgentSession.cs",
            "CopilotSdkRuntimeSession.cs",
            "LegacyTerminalWindowHost.cs",
            "PhotinoWindowHost.cs",
            "TerminalAdapter.cs",
            "WindowWatchdog.cs",
        };

        var present = forbiddenFiles
            .Where(file => File.Exists(Path.Combine(coreDirectory, file)))
            .ToArray();
        Assert.That(present, Is.Empty);
    }

    [Then("headquarters owns runtime composition without a bootstrap project")]
    public void ThenHeadquartersOwnsRuntimeCompositionWithoutABootstrapProject()
    {
        var headquartersProject = File.ReadAllText(Path.Combine(
            myWorkspace.RepositoryRootPath,
            "src",
            "squad-hq",
            "squad-hq.csproj"));
        var solution = File.ReadAllText(Path.Combine(myWorkspace.RepositoryRootPath, "squad.slnx"));

        Assert.Multiple(() =>
        {
            Assert.That(headquartersProject, Does.Contain("squad.CopilotSdk\\squad.CopilotSdk.csproj"));
            Assert.That(headquartersProject, Does.Contain("squad.Photino\\squad.Photino.csproj"));
            Assert.That(headquartersProject, Does.Not.Contain("squad.Bootstrap"));
            Assert.That(solution, Does.Not.Contain("squad.Bootstrap"));
            Assert.That(
                File.Exists(Path.Combine(
                    myWorkspace.RepositoryRootPath,
                    "src",
                    "squad.Bootstrap",
                    "squad.Bootstrap.csproj")),
                Is.False);
        });
    }

    [Then("every reachable squad production assembly and type is agent-safe")]
    public void ThenEveryReachableSquadProductionAssemblyAndTypeIsAgentSafe()
    {
        var assemblies = ReachableSquadAssemblies(Assembly.Load("squad"));
        Assert.That(
            assemblies.Select(assembly => assembly.GetName().Name),
            Is.EquivalentTo(new[] { "squad", "squad.Agent" }));

        var agentTypes = Assembly.Load("squad.Agent")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Agent")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(agentTypes, Is.EqualTo(new[]
        {
            "CliExitException",
            "CurrentRoleResolver",
            "HandoffHeaders",
            "HandoffQueue",
            "Priority",
            "ProcessResult",
            "ProcessRunner",
            "ProjectRoot",
            "RoleRow",
            "SequenceCounter",
            "SiblingTool",
            "SquadConfig",
            "Timestamps",
        }));

        var processMethods = typeof(ProcessRunner)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(processMethods, Is.EqualTo(new[]
        {
            "Run",
            "RunChecked",
        }));

        var projectRootMethods = typeof(ProjectRoot)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(projectRootMethods, Is.EqualTo(new[]
        {
            "ResolveProjectRoot",
            "ResolveViaGit",
        }));
    }

    [Then("no headquarters-only helper is reachable from squad")]
    public void ThenNoHeadquartersOnlyHelperIsReachableFromSquad()
    {
        AssertNoReachableTypes("Crc32", "HostProjectRoot", "ProjectLayout");
    }

    [Then("no host lifecycle, backend runtime, or UI contract is reachable from squad")]
    public void ThenNoHostLifecycleBackendRuntimeCleanupOrUiContractIsReachableFromSquad()
    {
        AssertNoReachableTypes(
            "HostControlClient",
            "HostLease",
            "IAgentBackend",
            "IAgentSession",
            "IRuntimeModeFactory",
            "ISquadUi",
            "IWindowHost",
            "ProcessControl",
            "RuntimeMode",
            "SessionRegistry",
            "SquadApplication",
            "SquadViewModel");
    }

    [Then("handoff, context, ready-for-next, and done-with-current remain available")]
    public void ThenAgentCommandsRemainAvailable()
    {
        var commandTypes = Assembly.Load("squad")
            .GetTypes()
            .Where(type => type.Namespace == "squad.Commands" && !type.IsNested)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(commandTypes, Is.EqualTo(new[]
        {
            "Context",
            "DoneWithCurrent",
            "DoneWithCurrentBatch",
            "DoneWithCurrentTask",
            "Handoff",
            "ReadyForNext",
            "ReadyForNextBatch",
            "ReadyForNextTask",
        }));
    }

    [Then("squad-hq retains launch, shutdown, and wait-for-agent behavior")]
    public void ThenSquadHqRetainsItsLifecycleBehavior()
    {
        var lifecycleTypes = Assembly.Load("squad-hq")
            .GetTypes()
            .Where(type => type.Namespace == "squad_hq.Commands")
            .Select(type => type.Name)
            .ToArray();

        Assert.That(
            lifecycleTypes,
            Does            .Contain("Launch")
            .And.Contain("Shutdown")
            .And.Contain("WaitForAgent"));
    }

    [Then("production source does not use the legacy backend selector")]
    public void ThenProductionSourceDoesNotUseTheLegacyBackendSelector()
    {
        var sourceDirectory = Path.Combine(myWorkspace.RepositoryRootPath, "src");
        var matches = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}squad.Specs{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("BLAXQUAD_USE_COPILOT_SDK", StringComparison.Ordinal))
            .ToArray();

        Assert.That(matches, Is.Empty);
    }

    [Then("the installers declare every supported runtime identifier")]
    public void ThenTheInstallersDeclareEverySupportedRuntimeIdentifier()
    {
        var shellInstaller = File.ReadAllText(Path.Combine(myWorkspace.RepositoryRootPath, "install.sh"));
        var powerShellInstaller = File.ReadAllText(Path.Combine(myWorkspace.RepositoryRootPath, "install.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(shellInstaller, Does.Contain("linux-x64"));
            Assert.That(shellInstaller, Does.Contain("osx-x64"));
            Assert.That(shellInstaller, Does.Contain("osx-arm64"));
            Assert.That(powerShellInstaller, Does.Contain("win-x64"));
        });
    }

    private static void AssertNoReachableTypes(params string[] forbiddenTypeNames)
    {
        var reachableTypeNames = ReachableSquadAssemblies(Assembly.Load("squad"))
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.Name)
            .ToArray();

        Assert.That(reachableTypeNames, Has.None.Matches<string>(forbiddenTypeNames.Contains));
    }

    private static IReadOnlyCollection<Assembly> ReachableSquadAssemblies(Assembly root)
    {
        var pending = new Queue<Assembly>();
        var visited = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        pending.Enqueue(root);
        while (pending.TryDequeue(out var assembly))
        {
            var name = assembly.GetName().Name!;
            if (!visited.TryAdd(name, assembly))
                continue;
            foreach (var reference in assembly.GetReferencedAssemblies()
                         .Where(reference => reference.Name?.StartsWith("squad", StringComparison.Ordinal) == true))
                pending.Enqueue(Assembly.Load(reference));
        }
        return visited.Values;
    }
}



