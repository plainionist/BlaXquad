namespace squad.Abstractions.Agents;

public sealed record AgentIdleEvent(DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);




