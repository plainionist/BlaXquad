using squad.Hosting.Abstractions;

namespace squad.Hosting.Stdio;

/// <summary>Sole public type of this plug-in, loaded at process startup through an explicit "--hosting" descriptor.
/// This project is a test fixture distributed to the backend acceptance suite, not a default headquarters
/// packaging input: it is never a supported end-user UI mode, and must never require any Photino type, assembly,
/// asset, or native dependency.</summary>
public sealed class StdioHostingFactory : IHostingFactory
{
    public string Name => "stdio";

    public HostingRuntime Create(HostingContext context) =>
        new(new StdioWindowHost(context.Ui, context.IssueCatalog, context.WorkspaceTools), new NoOpSleepInhibitor());
}
