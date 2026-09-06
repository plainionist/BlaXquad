namespace squad.AgentProvider.Abstractions;

/// <summary>Provides a backend with one role's resolved worktree, startup instruction, and agent settings.</summary>
public sealed record AgentRoleContext(
    string Role,
    string DisplayName,
    string WorktreePath,
    string InitialInstruction,
    string Permissions,
    string? Model,
    string? Effort);


