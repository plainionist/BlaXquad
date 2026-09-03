namespace squad.Abstractions.Agents;

public sealed record AgentReadinessEvent(
    DateTimeOffset OccurredAt,
    long Generation,
    string State,
    string? Error = null) : AgentEvent(OccurredAt);




