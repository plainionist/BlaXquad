namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentSessionUsageEvent(DateTimeOffset OccurredAt, decimal AicUsed) : AgentEvent(OccurredAt);




