namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolStartedEvent(
    DateTimeOffset OccurredAt,
    string ToolCallId,
    string ToolName,
    string? Arguments = null,
    bool IsRead = false,
    string? WorkingDirectory = null) : AgentEvent(OccurredAt);




