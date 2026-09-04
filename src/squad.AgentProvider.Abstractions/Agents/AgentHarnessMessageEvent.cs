namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentHarnessMessageEvent(DateTimeOffset OccurredAt, string Content) : AgentEvent(OccurredAt);




