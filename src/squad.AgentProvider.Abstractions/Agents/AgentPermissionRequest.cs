namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentPermissionRequest(DateTimeOffset OccurredAt, string RequestId, string Description) : AgentEvent(OccurredAt);




