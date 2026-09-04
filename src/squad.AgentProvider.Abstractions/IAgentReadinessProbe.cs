using squad.AgentProvider.Abstractions.Agents;

namespace squad.AgentProvider.Abstractions;

public interface IAgentReadinessProbe
{
    Task<AgentReadinessEvent?> ObserveReadinessAsync(CancellationToken cancellationToken = default);
    void InvalidateReadiness();
    bool IsReadinessGenerationCurrent(long generation);
}



