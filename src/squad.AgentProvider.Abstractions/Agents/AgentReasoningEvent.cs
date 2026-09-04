namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentReasoningEvent(DateTimeOffset OccurredAt, string Content, bool IsDelta) : AgentEvent(OccurredAt);




