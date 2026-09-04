namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentErrorEvent(DateTimeOffset OccurredAt, string Message) : AgentEvent(OccurredAt);




