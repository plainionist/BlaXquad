namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentPermissionRequest(DateTimeOffset OccurredAt, InteractionRequestId RequestId, string Description) : AgentEvent(OccurredAt);




