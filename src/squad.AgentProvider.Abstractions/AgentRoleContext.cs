namespace squad.AgentProvider.Abstractions;

public sealed record AgentRoleContext(
    string Role,
    string DisplayName,
    string WorktreePath,
    string InitialInstruction,
    string Permissions,
    string? Model,
    string? Effort);



