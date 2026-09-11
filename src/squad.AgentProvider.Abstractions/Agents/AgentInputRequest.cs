namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentInputRequest(DateTimeOffset OccurredAt, string RequestId, string Prompt, IReadOnlyList<string>? Choices = null, bool AllowFreeform = true) : AgentEvent(OccurredAt);




