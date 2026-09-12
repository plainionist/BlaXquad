using squad.Domain;

namespace squad.Configuration;

/// <summary>One configured squad member: a unique operational identity plus its role reference, worktree target,
/// receive mode, and agent settings. Multiple members may reference the same role.</summary>
public sealed record SquadMemberConfiguration(
    SquadMemberId Name,
    string DisplayName,
    RoleId Role,
    WorktreeTarget WorktreeTarget,
    ReceiveMode ReceiveMode,
    AgentSettings Agent);


