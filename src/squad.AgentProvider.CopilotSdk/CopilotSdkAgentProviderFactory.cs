using squad.AgentProvider.Abstractions;

namespace squad.AgentProvider.CopilotSdk;

/// <summary>Exposes the Copilot SDK implementation through the provider-neutral agent-provider contract.</summary>
public sealed class CopilotSdkAgentProviderFactory : IAgentProviderFactory
{
    public string Name => "sdk";

    public Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IAgentBackend backend = new CopilotSdkBackend(context);
        return Task.FromResult(backend);
    }
}
