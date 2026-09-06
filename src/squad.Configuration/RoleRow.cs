namespace squad.Configuration;

/// <summary>Combines command-side role configuration with its resolved worktree path.</summary>
public sealed record RoleRow(
    string Role,
    string WorktreeName,
    string WorktreePath,
    string DisplayName,
    string ReceiveMode);


