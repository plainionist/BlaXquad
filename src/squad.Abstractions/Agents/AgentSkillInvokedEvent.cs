namespace squad.Abstractions.Agents;

public sealed record AgentSkillInvokedEvent(
    DateTimeOffset OccurredAt,
    string Name) : AgentEvent(OccurredAt);