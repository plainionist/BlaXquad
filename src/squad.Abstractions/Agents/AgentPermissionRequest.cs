namespace squad.Abstractions.Agents;

public sealed record AgentPermissionRequest(DateTimeOffset OccurredAt, string RequestId, string Role, string Description) : AgentEvent(OccurredAt);




