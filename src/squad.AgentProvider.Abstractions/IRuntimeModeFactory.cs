namespace squad.AgentProvider.Abstractions;

public interface IRuntimeModeFactory
{
    string Name { get; }
    bool IsAvailable { get; }
    IAgentBackend CreateBackend(Func<AgentBackendContext> context);
    Task PrepareAsync(Func<AgentBackendContext> context, CancellationToken cancellationToken);
}



