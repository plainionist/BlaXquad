namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolStartedEvent(
    DateTimeOffset OccurredAt,
    string ToolCallId,
    string ToolName,
    string? Arguments = null,
    string? Kind = null,
    string? WorkingDirectory = null) : AgentEvent(OccurredAt);




