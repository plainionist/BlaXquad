namespace squad.Workspaces;

/// <summary>Represents validated role settings after resolution to a concrete launch worktree.</summary>
public record RoleConfigRow(
    string Role,
    string DisplayName,
    string WorktreeName,
    string WorktreePath,
    string ReceiveMode,
    string Permissions = "prompt",
    string? Model = null,
    string? Effort = null);


