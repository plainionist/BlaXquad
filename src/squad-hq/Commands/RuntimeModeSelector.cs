using global::squad.AgentProvider.Abstractions;
using global::squad.CopilotSdk;
using global::squad.Photino;
using global::squad.Ui.Abstractions;

namespace squadHQ.Commands;

public static class RuntimeModeSelector
{
    private static readonly CopilotSdkRuntimeModeFactory myFactory = new();

    internal static RuntimeMode Create(Func<AgentBackendContext> context, ISquadUi ui)
    {
        var backend = myFactory.CreateBackend(context);
        return new RuntimeMode(
            backend,
            new PhotinoWindowHost(ui, context().WorkingDirectory),
            new SleepInhibitor(),
            cancellationToken => myFactory.PrepareAsync(context, cancellationToken));
    }

    public static bool TryRunPrivateCommand(string command, string[] arguments, out int exitCode)
    {
        return myFactory.TryRunPrivateCommand(command, arguments, out exitCode);
    }

    public static string WindowTitle(string workspaceDirectory) =>
        PhotinoWindowHost.CreateTitle(workspaceDirectory);
}



