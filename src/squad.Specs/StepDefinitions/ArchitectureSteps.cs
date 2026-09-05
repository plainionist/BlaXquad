using global::squad.Specs.Support;
using global::squad.Agent.Cli;
using global::squad.Configuration;
using global::squad.Agent.Handoff;
using global::squad.Process;
using global::squad.AgentProvider.Abstractions;
using global::squad.Core;
using global::squad.Core.Handoffs;
using global::squad.Core.Transcripts;
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
            Is.EquivalentTo(new[] { "squad", "squad.Agent.Cli", "squad.Configuration", "squad.Agent.Handoff", "squad.Process" }));

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

        var agentConfigurationTypes = Assembly.Load("squad.Configuration")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Configuration")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(agentConfigurationTypes, Is.EqualTo(new[]
        {
            "CurrentRoleResolver",
            "ProjectRoot",
            "RoleRow",
            "SquadConfig",
        }));

        var agentHandoffTypes = Assembly.Load("squad.Agent.Handoff")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Agent.Handoff")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(agentHandoffTypes, Is.EqualTo(new[]
        {
            "HandoffHeaders",
            "HandoffQueue",
            "Priority",
            "SequenceCounter",
            "Timestamps",
        }));

        var agentProcessTypes = Assembly.Load("squad.Process")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Process")
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

        // squad.Agent.Tooling is only reachable from squad-hq, not from squad, but it is still one of the
        // extracted agent-safe assemblies and must keep its single-responsibility type list.
        var agentToolingTypes = Assembly.Load("squad.Agent.Tooling")
            .GetTypes()
            .Where(type => !type.IsNested && type.Namespace == "squad.Agent.Tooling")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(agentToolingTypes, Is.EqualTo(new[]
        {
            "SiblingTool",
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
    public void ThensquadHQRetainsItsLifecycleBehavior()
    {
        var lifecycleTypes = Assembly.Load("squad-hq")
            .GetTypes()
            .Where(type => type.Namespace == "squadHQ.Commands")
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

    [Then("the handoff delivery assembly depends only on agent configuration and handoff contracts")]
    public void ThenTheHandoffDeliveryAssemblyDependsOnlyOnAgentConfigurationAndHandoffContracts()
    {
        var references = ReferencedSquadAssemblyNames(typeof(InProcessHandoffPoller).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(references, Does.Contain("squad.Configuration"));
            Assert.That(references, Does.Contain("squad.Agent.Handoff"));
            Assert.That(references, Does.Not.Contain("squad.Core"));
            Assert.That(references, Does.Not.Contain("squad.AgentProvider.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Ui.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Hosting.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Photino"));
            Assert.That(references, Does.Not.Contain("squad.CopilotSdk"));
        });
    }

    [Then("the application core no longer depends on agent configuration or handoff contracts")]
    public void ThenTheApplicationCoreNoLongerDependsOnAgentConfigurationOrHandoffContracts()
    {
        var references = ReferencedSquadAssemblyNames(typeof(SquadViewModel).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(references, Does.Not.Contain("squad.Configuration"));
            Assert.That(references, Does.Not.Contain("squad.Agent.Handoff"));
        });
    }

    [Then("the transcript assembly depends only on UI abstractions")]
    public void ThenTheTranscriptAssemblyDependsOnlyOnUiAbstractions()
    {
        var references = ReferencedSquadAssemblyNames(typeof(RoleTranscriptState).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(references, Does.Contain("squad.Ui.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Core"));
            Assert.That(references, Does.Not.Contain("squad.Core.Handoffs"));
            Assert.That(references, Does.Not.Contain("squad.AgentProvider.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Agent.Configuration"));
            Assert.That(references, Does.Not.Contain("squad.Agent.Handoff"));
            Assert.That(references, Does.Not.Contain("squad.Hosting.Abstractions"));
            Assert.That(references, Does.Not.Contain("squad.Photino"));
            Assert.That(references, Does.Not.Contain("squad.CopilotSdk"));
        });
    }

    [Then("the application core depends on the transcript assembly")]
    public void ThenTheApplicationCoreDependsOnTheTranscriptAssembly()
    {
        var references = ReferencedSquadAssemblyNames(typeof(SquadViewModel).Assembly);

        Assert.That(references, Does.Contain("squad.Core.Transcripts"));
    }

    [Then("the application core depends on exactly the agent provider abstraction, the UI abstraction, and the transcript assembly")]
    public void ThenTheApplicationCoreDependsOnExactlyTheAllowedAssemblies()
    {
        var references = ReferencedSquadAssemblyNames(typeof(SquadViewModel).Assembly);

        Assert.That(references, Is.EquivalentTo(new[]
        {
            "squad.AgentProvider.Abstractions",
            "squad.Ui.Abstractions",
            "squad.Core.Transcripts",
        }));
    }

    [Then("the transcript and handoff assemblies do not depend on the application core")]
    public void ThenTheTranscriptAndHandoffAssembliesDoNotDependOnTheApplicationCore()
    {
        var transcriptReferences = ReferencedSquadAssemblyNames(typeof(RoleTranscriptState).Assembly);
        var handoffReferences = ReferencedSquadAssemblyNames(typeof(InProcessHandoffPoller).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(transcriptReferences, Does.Not.Contain("squad.Core"));
            Assert.That(handoffReferences, Does.Not.Contain("squad.Core"));
        });
    }

    [Then("headquarters composes the application core, transcript assembly, and handoff assembly without a reverse dependency")]
    public void ThenHeadquartersComposesCoreModulesWithoutAReverseDependency()
    {
        var headquartersReferences = ReferencedSquadAssemblyNames(Assembly.Load("squad-hq"));

        Assert.Multiple(() =>
        {
            Assert.That(headquartersReferences, Does.Contain("squad.Core"));
            Assert.That(headquartersReferences, Does.Contain("squad.Core.Handoffs"));
            Assert.That(ReferencedSquadAssemblyNames(typeof(SquadViewModel).Assembly), Does.Not.Contain("squad-hq"));
            Assert.That(ReferencedSquadAssemblyNames(typeof(RoleTranscriptState).Assembly), Does.Not.Contain("squad-hq"));
            Assert.That(ReferencedSquadAssemblyNames(typeof(InProcessHandoffPoller).Assembly), Does.Not.Contain("squad-hq"));
        });
    }

    [Then("role-operation, interaction, and event-projection coordinators are internal modules owned by the application core")]
    public void ThenCoordinatorTypesAreInternalCoreModules()
    {
        var coreAssembly = typeof(SquadViewModel).Assembly;

        AssertInternalCoreType(coreAssembly, "squad.Core.RoleOperations.RoleOperationCoordinator");
        AssertInternalCoreType(coreAssembly, "squad.Core.Interactions.PendingInteractionRegistry");
        AssertInternalCoreType(coreAssembly, "squad.Core.Events.AgentEventProjector");
    }

    [Then("transcript and handoff implementation types are owned only by their extracted assemblies")]
    public void ThenTranscriptAndHandoffImplementationTypesAreOwnedOnlyByTheirExtractedAssemblies()
    {
        var coreAssembly = typeof(SquadViewModel).Assembly;
        var coreTypeNames = coreAssembly.GetTypes().Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var transcriptAssembly = typeof(RoleTranscriptState).Assembly;
        var handoffAssembly = typeof(InProcessHandoffPoller).Assembly;

        var transcriptImplementationTypes = new[]
        {
            "RoleTranscriptState", "TranscriptArchive", "TranscriptEntryBuffer", "TranscriptRetentionOptions", "ToolCompletionResult",
        };
        var handoffImplementationTypes = new[] { "HandoffDeliveryService", "InProcessHandoffPoller" };

        Assert.Multiple(() =>
        {
            foreach (var typeName in transcriptImplementationTypes)
            {
                Assert.That(coreTypeNames, Does.Not.Contain(typeName), $"{typeName} should not be defined in squad.Core.");
                Assert.That(
                    transcriptAssembly.GetType($"squad.Core.Transcripts.{typeName}"),
                    Is.Not.Null,
                    $"{typeName} should be defined in squad.Core.Transcripts.");
            }
            foreach (var typeName in handoffImplementationTypes)
            {
                Assert.That(coreTypeNames, Does.Not.Contain(typeName), $"{typeName} should not be defined in squad.Core.");
                Assert.That(
                    handoffAssembly.GetType($"squad.Core.Handoffs.{typeName}"),
                    Is.Not.Null,
                    $"{typeName} should be defined in squad.Core.Handoffs.");
            }
        });
    }

    private static void AssertInternalCoreType(Assembly coreAssembly, string fullName)
    {
        var type = coreAssembly.GetType(fullName, throwOnError: false);
        Assert.That(type, Is.Not.Null, $"{fullName} should be defined in {coreAssembly.GetName().Name}.");
        Assert.That(type!.IsPublic, Is.False, $"{fullName} should not be public.");
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



