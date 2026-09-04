namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentSystemMessageEvent(DateTimeOffset OccurredAt, string Content) : AgentEvent(OccurredAt);




