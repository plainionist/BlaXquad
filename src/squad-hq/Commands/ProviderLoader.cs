using squad.AgentProvider.Abstractions;
using squad.Process;

namespace squadHQ.Commands;

/// <summary>Loads the <see cref="IAgentProviderFactory"/> selected by a <see cref="ProviderDescriptor"/> into
/// its own assembly load context, kept alive for the remainder of the process, via the plug-in mechanics shared
/// with <see cref="HostingLoader"/>.</summary>
static class ProviderLoader
{
    // Must unify with the host's own copy, so it is never resolved locally even if a copy of it
    // happens to sit beside the provider assembly on disk (e.g. because it is also the host's own dependency).
    // squad.Domain is included because IAgentSession.MemberId is typed SquadMemberId, so it crosses the provider
    // boundary too even though the value only ever originates from the host's own configuration.
    private static readonly HashSet<string> SharedAssemblyNames = ["squad.AgentProvider.Abstractions", "squad.Domain"];

    // Keeps every provider load context reachable so it (and the assemblies it loaded) survive for the process lifetime.
    private static readonly List<PluginLoadContext> myLoadContexts = [];

    private static readonly PluginLoaderDiagnostics Diagnostics = new(
        AssemblyNotFound: path => new CliExitException(1, $"Provider assembly not found: '{path}'."),
        AssemblyLoadFailed: (path, exception) => new CliExitException(1, $"Provider assembly could not be loaded: '{path}'.", exception),
        TypeIncompatible: (typeName, path) => new CliExitException(1,
            $"Provider type '{typeName}' was not found in '{path}', or does not " +
            $"publicly implement {nameof(IAgentProviderFactory)}."),
        MissingConstructor: typeName => new CliExitException(1, $"Provider type '{typeName}' has no public parameterless constructor."),
        ConstructorFailed: (typeName, exception) => new CliExitException(1, $"Provider type '{typeName}' threw during construction.", exception));

    public static IAgentProviderFactory Load(ProviderDescriptor descriptor) =>
        PluginLoader.Load<IAgentProviderFactory>(
            descriptor.AssemblyPath, descriptor.TypeName, SharedAssemblyNames, myLoadContexts, Diagnostics);
}
