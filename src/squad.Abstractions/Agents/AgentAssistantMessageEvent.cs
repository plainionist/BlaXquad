namespace squad.Abstractions.Agents;

public sealed record AgentAssistantMessageEvent(DateTimeOffset OccurredAt, string Content, bool IsDelta) : AgentEvent(OccurredAt);




