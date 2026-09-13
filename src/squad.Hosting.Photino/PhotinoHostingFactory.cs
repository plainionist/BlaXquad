using squad.Hosting.Abstractions;

namespace squad.Hosting.Photino;

/// <summary>Sole public type of this plug-in, loaded at process startup either as headquarters' packaged default
/// hosting bundle (when "--hosting" is omitted) or through an explicit "--hosting" descriptor. squad-hq never
/// references <see cref="PhotinoWindowHost"/> or <see cref="squad.Hosting.Photino.SleepInhibitor"/> directly; both
/// stay internal to this assembly.</summary>
public sealed class PhotinoHostingFactory : IHostingFactory
{
    public string Name => "photino";

    public HostingRuntime Create(HostingContext context) =>
        new(
            new PhotinoWindowHost(context.Ui, context.IssueCatalog, context.WorkspaceTools, context.WorkingDirectory, context.LoggerFactory),
            new SleepInhibitor());
}
