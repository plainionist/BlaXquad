namespace squad.Workspaces;

/// <summary>Represents one validated configured squad member after resolution to a concrete launch worktree. Carries
/// both the member's own unique identity and its referenced (possibly shared) role.</summary>
public record MemberConfigRow(
    string Member,
    string DisplayName,
    string Role,
    string WorktreeName,
    string WorktreePath,
    string ReceiveMode,
    string Permissions = "prompt",
    string? Model = null,
    string? Effort = null);
