namespace squad.Abstractions.Agents;

public sealed record AgentContextUsageEvent(DateTimeOffset OccurredAt, long UsedTokens, long LimitTokens) : AgentEvent(OccurredAt);




