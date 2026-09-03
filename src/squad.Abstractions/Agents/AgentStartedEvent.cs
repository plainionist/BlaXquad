namespace squad.Abstractions.Agents;

public sealed record AgentStartedEvent(DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);




