using global::squad.AgentProvider.Abstractions;

namespace squad.CopilotSdk;

public sealed class CopilotSdkRuntimeModeFactory : IRuntimeModeFactory
{
    public string Name => "sdk";
    public bool IsAvailable => true;

    public IAgentBackend CreateBackend(Func<AgentBackendContext> context) =>
        new CopilotSdkBackend(context);

    public Task PrepareAsync(Func<AgentBackendContext> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public bool TryRunPrivateCommand(string command, string[] arguments, out int exitCode)
    {
        exitCode = 0;
        return false;
    }
}



