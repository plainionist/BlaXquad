namespace squad.AgentProvider.Abstractions;

/// <summary>Provides a backend with one configured member's unique identity, referenced role (used only to
/// select role-owned prompt content upstream), resolved worktree, startup instruction, and agent settings.
/// Multiple members may share the same <see cref="Role"/>.</summary>
public sealed record AgentMemberContext(
    string Member,
    string Role,
    string DisplayName,
    string WorktreePath,
    string InitialInstruction,
    string Permissions,
    string? Model,
    string? Effort);
