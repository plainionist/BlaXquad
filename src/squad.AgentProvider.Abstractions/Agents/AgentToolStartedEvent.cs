namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolStartedEvent(
    DateTimeOffset OccurredAt,
    ToolCallId ToolCallId,
    string ToolName,
    string? Arguments = null,
    bool IsRead = false,
    string? WorkingDirectory = null) : AgentEvent(OccurredAt);




