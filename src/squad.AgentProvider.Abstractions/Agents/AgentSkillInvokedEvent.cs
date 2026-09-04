namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentSkillInvokedEvent(
    DateTimeOffset OccurredAt,
    string Name) : AgentEvent(OccurredAt);