using squad.Domain;

namespace squad.Configuration;

/// <summary>The fully validated schema-version-2 configuration: an immutable catalog of reusable role identities, the
/// ordered members that reference them, the leader member's identity, safe shared worktree paths, and the optional
/// Git-history command.</summary>
public sealed record SquadConfiguration(
    IReadOnlyList<RoleId> Roles,
    IReadOnlyList<SquadMemberConfiguration> Members,
    SquadMemberId Leader,
    IReadOnlyList<string> SharedWorktreePaths,
    IReadOnlyList<string>? GitHistoryCommand);
