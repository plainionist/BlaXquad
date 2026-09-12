namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolOutputChangedEvent(
    DateTimeOffset OccurredAt,
    ToolCallId ToolCallId,
    string Output) : AgentEvent(OccurredAt);




