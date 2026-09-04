using global::squad.Specs.Support;
using global::squad.Agent;
using global::squad.Agent.Cli;
using global::squad.Agent.Process;
using global::squad.AgentProvider.Abstractions;
using global::squad.Core;
using global::squad.CopilotSdk;
using global::squad.Hosting.Abstractions;
using global::squad.Photino;
using global::squad.Ui.Abstractions;
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
            Is.EquivalentTo(new[] { "squad", "squad.Agent", "squad.Agent.Cli", "squad.Agent.Process" }));

        var agentTypes = Assembly.Load("squad.Agent")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Agent")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(agentTypes, Is.EqualTo(new[]
        {
            "CurrentRoleResolver",
            "HandoffHeaders",
            "HandoffQueue",
            "Priority",
            "ProjectRoot",
            "RoleRow",
            "SequenceCounter",
            "SiblingTool",
            "SquadConfig",
            "Timestamps",
        }));

        var agentCliTypes = Assembly.Load("squad.Agent.Cli")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Agent.Cli")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(agentCliTypes, Is.EqualTo(new[]
        {
            "CliExitException",
        }));

        var agentProcessTypes = Assembly.Load("squad.Agent.Process")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Agent.Process")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(agentProcessTypes, Is.EqualTo(new[]
        {
            "ProcessResult",
            "ProcessRunner",
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

    [Then("squad.Abstractions is no longer part of the solution")]
    public void ThenSquadAbstractionsIsNoLongerPartOfTheSolution()
    {
        var solution = File.ReadAllText(Path.Combine(myWorkspace.RepositoryRootPath, "squad.slnx"));
        var projectDirectory = Path.Combine(myWorkspace.RepositoryRootPath, "src", "squad.Abstractions");

        Assert.Multiple(() =>
        {
            Assert.That(solution, Does.Not.Contain("squad.Abstractions"));
            Assert.That(Directory.Exists(projectDirectory), Is.False);
        });
    }

    [Then("the application core depends on the agent provider and UI abstractions but not on hosting or presentation adapters")]
    public void ThenTheApplicationCoreDependsOnlyOnAllowedContracts()
    {
        var references = ReferencedSquadAssemblyNames(typeof(SquadViewModel).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(references, Does.Contain("squad.AgentProvider.Abstractions"));
            Assert.That(references, Does.Contain("squad.Ui.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Hosting.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Photino"));
            Assert.That(references, Does.Not.Contain("squad.CopilotSdk"));
        });
    }

    [Then("the copilot sdk adapter depends only on the agent provider abstraction")]
    public void ThenTheCopilotSdkAdapterDependsOnlyOnTheAgentProviderAbstraction()
    {
        var references = ReferencedSquadAssemblyNames(typeof(CopilotSdkRuntimeModeFactory).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(references, Does.Contain("squad.AgentProvider.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Ui.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Hosting.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Core"));
            Assert.That(references, Does.Not.Contain("squad.Photino"));
        });
    }

    [Then("the photino adapter depends on the UI and hosting abstractions but not directly on the agent provider or copilot sdk adapter")]
    public void ThenThePhotinoAdapterDependsOnlyOnAllowedContracts()
    {
        var references = ReferencedSquadAssemblyNames(typeof(SleepInhibitor).Assembly);
        var photinoProject = File.ReadAllText(Path.Combine(
            myWorkspace.RepositoryRootPath, "src", "squad.Photino", "squad.Photino.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(references, Does.Contain("squad.Ui.Abstractions"));
            Assert.That(references, Does.Contain("squad.Hosting.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.CopilotSdk"));
            Assert.That(references, Does.Not.Contain("squad.Core"));
            Assert.That(photinoProject, Does.Not.Contain("squad.AgentProvider.Abstractions"));
        });
    }

    [Then("the agent provider and hosting abstractions do not depend on presentation or provider adapters")]
    public void ThenTheAgentProviderAndHostingAbstractionsRemainIndependentOfAdapters()
    {
        var agentProviderReferences = ReferencedSquadAssemblyNames(typeof(IRuntimeModeFactory).Assembly);
        var hostingReferences = ReferencedSquadAssemblyNames(typeof(IWindowHost).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(agentProviderReferences, Is.Empty);
            Assert.That(hostingReferences, Is.Empty);
        });
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

    private static string[] ReferencedSquadAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("squad", StringComparison.Ordinal) == true)
            .Select(name => name!)
            .ToArray();
}



