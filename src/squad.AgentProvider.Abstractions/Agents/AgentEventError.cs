namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentEventError(DateTimeOffset OccurredAt, string Message) : AgentEvent(OccurredAt);




