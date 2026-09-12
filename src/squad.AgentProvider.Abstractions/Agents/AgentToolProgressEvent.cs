namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolProgressEvent(
    DateTimeOffset OccurredAt,
    ToolCallId ToolCallId,
    string Progress) : AgentEvent(OccurredAt);




