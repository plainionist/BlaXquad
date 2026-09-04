namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentToolCompletedEvent(
	DateTimeOffset OccurredAt,
	string ToolCallId,
	string ToolName,
	bool Succeeded,
	string? DisplayOutputFallback = null,
	string? ContentFallback = null) : AgentEvent(OccurredAt);




