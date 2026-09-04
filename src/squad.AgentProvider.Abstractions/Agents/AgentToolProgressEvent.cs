namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolProgressEvent(
    DateTimeOffset OccurredAt,
    string ToolCallId,
    string Progress) : AgentEvent(OccurredAt);




