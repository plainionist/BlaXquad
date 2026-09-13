using squad.Hosting.Abstractions;
using squad.Process;

namespace squadHQ.Commands;

/// <summary>Loads the <see cref="IHostingFactory"/> selected by a <see cref="HostingDescriptor"/> into its own
/// assembly load context, kept alive for the remainder of the process, via the plug-in mechanics shared with
/// <see cref="ProviderLoader"/>.</summary>
static class HostingLoader
{
    // Every contract assembly whose types cross the hosting boundary must unify with the host's own copy rather
    // than loading a duplicate, type-incompatible copy - even one a plug-in's own publish output happens to carry
    // beside its assembly (e.g. because squad.Ui.Abstractions is also that plug-in's own build dependency).
    // squad.AgentProvider.Abstractions is included because ISquadUi's own signatures (e.g. GetPendingElicitation)
    // reference its types, so it crosses the hosting boundary too even though hosting never loads a provider.
    // squad.Domain is included for the same reason: ISquadUi's member-command signatures now take SquadMemberId,
    // so a hosting plug-in that calls those methods (e.g. from squad.Ui.Protocol's UiCommandHandler) must resolve
    // SquadMemberId to the host's own type identity, not a private copy loaded into the plug-in's load context.
    // Microsoft.Extensions.Logging.Abstractions is included because HostingContext.LoggerFactory carries the
    // launch-owned ILoggerFactory across this same boundary, so a plug-in that calls CreateLogger<T>() on it must
    // resolve ILoggerFactory to the host's own type identity too.
    private static readonly HashSet<string> SharedAssemblyNames =
        [
            "squad.Hosting.Abstractions",
            "squad.Ui.Abstractions",
            "squad.AgentProvider.Abstractions",
            "squad.Domain",
            "Microsoft.Extensions.Logging.Abstractions",
        ];

    // Keeps every hosting load context reachable so it (and the assemblies it loaded) survive for the process lifetime.
    // Kept separate from ProviderLoader's own list so provider and hosting plug-ins never share a load context.
    private static readonly List<PluginLoadContext> myLoadContexts = [];

    private static readonly PluginLoaderDiagnostics Diagnostics = new(
        AssemblyNotFound: path => new CliExitException(1, $"Hosting assembly not found: '{path}'."),
        AssemblyLoadFailed: (path, exception) => new CliExitException(1, $"Hosting assembly could not be loaded: '{path}'.", exception),
        TypeIncompatible: (typeName, path) => new CliExitException(1,
            $"Hosting type '{typeName}' was not found in '{path}', or does not " +
            $"publicly implement {nameof(IHostingFactory)}."),
        MissingConstructor: typeName => new CliExitException(1, $"Hosting type '{typeName}' has no public parameterless constructor."),
        ConstructorFailed: (typeName, exception) => new CliExitException(1, $"Hosting type '{typeName}' threw during construction.", exception));

    public static IHostingFactory Load(HostingDescriptor descriptor) =>
        PluginLoader.Load<IHostingFactory>(
            descriptor.AssemblyPath, descriptor.TypeName, SharedAssemblyNames, myLoadContexts, Diagnostics);
}
