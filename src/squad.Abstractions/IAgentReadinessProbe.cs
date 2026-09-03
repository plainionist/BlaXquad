using squad.Abstractions.Agents;

namespace squad.Abstractions;

public interface IAgentReadinessProbe
{
    Task<AgentReadinessEvent?> ObserveReadinessAsync(CancellationToken cancellationToken = default);
    void InvalidateReadiness();
    bool IsReadinessGenerationCurrent(long generation);
}



