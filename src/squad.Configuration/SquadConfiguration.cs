namespace squad.Configuration;

/// <summary>The fully validated schema-version-2 configuration: an immutable catalog of reusable role names, the
/// ordered members that reference them, the leader member's name, and safe shared worktree paths.</summary>
public sealed record SquadConfiguration(
    IReadOnlyList<string> Roles,
    IReadOnlyList<SquadMemberConfiguration> Members,
    string Leader,
    IReadOnlyList<string> SharedWorktreePaths);



