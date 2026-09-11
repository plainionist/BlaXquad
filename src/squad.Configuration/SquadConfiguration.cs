namespace squad.Configuration;

/// <summary>The fully validated schema-version-2 configuration: an immutable catalog of reusable role names, the
/// ordered members that reference them, the leader member's name, safe shared worktree paths, and the optional
/// Git-history command.</summary>
public sealed record SquadConfiguration(
    IReadOnlyList<string> Roles,
    IReadOnlyList<SquadMemberConfiguration> Members,
    string Leader,
    IReadOnlyList<string> SharedWorktreePaths,
    IReadOnlyList<string>? GitHistoryCommand);
