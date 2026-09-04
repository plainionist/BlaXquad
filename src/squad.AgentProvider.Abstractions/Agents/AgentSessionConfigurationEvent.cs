namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentSessionConfigurationEvent(DateTimeOffset OccurredAt, string? Model, string? Effort) : AgentEvent(OccurredAt);




