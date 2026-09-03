namespace squad.Abstractions.Agents;

public sealed record AgentStoppedEvent(DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);




