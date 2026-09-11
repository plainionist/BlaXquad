namespace squad.Configuration;

/// <summary>One configured squad member: a unique operational identity plus its role reference, worktree, receive
/// mode, and agent settings. Multiple members may reference the same role.</summary>
public sealed record SquadMemberConfiguration(
    string Name,
    string DisplayName,
    string Role,
    string Worktree,
    string ReceiveMode,
    SquadAgentConfiguration Agent);


