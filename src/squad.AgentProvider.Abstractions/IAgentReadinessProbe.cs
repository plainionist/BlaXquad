using squad.AgentProvider.Abstractions.Agents;

namespace squad.AgentProvider.Abstractions;

/// <summary>
/// Provides an authoritative provider-side readiness observation when local event-derived state may be stale.
/// </summary>
public interface IAgentReadinessProbe
{
    /// <summary>Returns a fresh readiness event, or <see langword="null"/> when the provider cannot determine readiness.</summary>
    Task<AgentReadinessEvent?> ObserveReadinessAsync(CancellationToken cancellationToken = default);
    /// <summary>Invalidates cached readiness so an older observation cannot make a new operation appear ready.</summary>
    void InvalidateReadiness();
    /// <summary>Returns whether an observation still belongs to the latest readiness generation.</summary>
    bool IsReadinessGenerationCurrent(long generation);
}

