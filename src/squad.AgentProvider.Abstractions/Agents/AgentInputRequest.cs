namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentInputRequest(DateTimeOffset OccurredAt, InteractionRequestId RequestId, string Prompt, IReadOnlyList<string>? Choices = null, bool AllowFreeform = true) : AgentEvent(OccurredAt);




