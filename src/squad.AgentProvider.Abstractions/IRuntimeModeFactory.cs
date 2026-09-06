namespace squad.AgentProvider.Abstractions;

/// <summary>
/// Supplies a selectable agent runtime mode and performs any mode-specific preparation before a backend is used.
/// </summary>
public interface IRuntimeModeFactory
{
    string Name { get; }
    bool IsAvailable { get; }
    IAgentBackend CreateBackend(Func<AgentBackendContext> context);
    /// <summary>Completes prerequisite setup without starting a provider runtime or any role sessions.</summary>
    Task PrepareAsync(Func<AgentBackendContext> context, CancellationToken cancellationToken);
}


