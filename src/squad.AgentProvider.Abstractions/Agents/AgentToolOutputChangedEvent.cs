namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolOutputChangedEvent(
    DateTimeOffset OccurredAt,
    string ToolCallId,
    string Output) : AgentEvent(OccurredAt);




