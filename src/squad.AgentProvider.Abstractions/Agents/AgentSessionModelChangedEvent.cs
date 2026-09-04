namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentSessionModelChangedEvent(DateTimeOffset OccurredAt, string? Model, string? Effort) : AgentEvent(OccurredAt);




