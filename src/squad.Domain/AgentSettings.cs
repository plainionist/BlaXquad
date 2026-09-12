namespace squad.Domain;

/// <summary>One squad member's normalized agent permissions, model, and effort, independent of how they were
/// configured or serialized.</summary>
public sealed record AgentSettings(string Permissions, string? Model, string? Effort);
