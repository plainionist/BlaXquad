namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentStartedEvent(DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);




