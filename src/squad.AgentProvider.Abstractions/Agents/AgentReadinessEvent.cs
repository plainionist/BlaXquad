namespace squad.AgentProvider.Abstractions.Agents;

/// <summary>
/// Reports provider readiness for a specific generation so stale observations cannot overwrite newer operation
/// state.
/// </summary>
public sealed record AgentReadinessEvent(
    DateTimeOffset OccurredAt,
    long Generation,
    string State,
    string? Error = null) : AgentEvent(OccurredAt);



