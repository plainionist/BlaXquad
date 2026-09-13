using squad.Domain;

namespace squad.AgentProvider.Abstractions;

/// <summary>Provides a backend with one member's resolved worktree, startup instruction, and agent settings.</summary>
public sealed record AgentRoleContext(
    SquadMemberId MemberId,
    string DisplayName,
    string WorktreePath,
    string InitialInstruction,
    PermissionMode Permissions,
    string? Model,
    string? Effort);
