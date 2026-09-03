namespace squad.Abstractions.Agents;

public sealed record AgentInputRequest(DateTimeOffset OccurredAt, string RequestId, string Role, string Prompt, IReadOnlyList<string>? Choices = null, bool AllowFreeform = true) : AgentEvent(OccurredAt);




