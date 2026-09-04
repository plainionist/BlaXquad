namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentUserMessageEvent(DateTimeOffset OccurredAt, string Content) : AgentEvent(OccurredAt);




