namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentIdleEvent(DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);




