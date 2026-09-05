using global::squad.AgentProvider.Abstractions;
using global::squad.CopilotSdk;
using global::squad.Photino;
using global::squad.Ui.Abstractions;

namespace squadHQ.Commands;

public static class RuntimeModeSelector
{
    private static readonly CopilotSdkRuntimeModeFactory myFactory = new();

    public static IRuntimeModeFactory Select(string? value)
    {
        if (value is not null && !string.Equals(value, myFactory.Name, StringComparison.Ordinal))
            throw new InvalidOperationException("BLAXQUAD_AGENT_BACKEND must be unset or 'sdk'.");
        return myFactory;
    }

    internal static RuntimeMode Create(
        IRuntimeModeFactory factory,
        Func<AgentBackendContext> context,
        ISquadUi ui)
    {
        var backend = factory.CreateBackend(context);
        return new RuntimeMode(
            backend,
            new PhotinoWindowHost(ui, context().WorkingDirectory),
            new SleepInhibitor(),
            cancellationToken => factory.PrepareAsync(context, cancellationToken));
    }

    public static bool TryRunPrivateCommand(string command, string[] arguments, out int exitCode)
    {
        return myFactory.TryRunPrivateCommand(command, arguments, out exitCode);
    }

    public static string WindowTitle(string workspaceDirectory) =>
        PhotinoWindowHost.CreateTitle(workspaceDirectory);
}



