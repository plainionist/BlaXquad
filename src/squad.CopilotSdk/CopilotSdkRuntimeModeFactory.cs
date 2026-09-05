using squad.AgentProvider.Abstractions;

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
}



