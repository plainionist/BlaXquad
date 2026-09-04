namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentSubagentStartedEvent(
    DateTimeOffset OccurredAt,
    string? AgentName,
    string? AgentDisplayName,
    string? Model) : AgentEvent(OccurredAt);
