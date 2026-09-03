namespace squad.Abstractions.Agents;

public sealed record AgentSystemMessageEvent(DateTimeOffset OccurredAt, string Content) : AgentEvent(OccurredAt);




