namespace squad.Domain;

/// <summary>One immutable, resolved description of a configured squad member: its own operational identity, the
/// reusable role it references, its resolved worktree, receive mode, and normalized agent settings. This is the
/// single canonical member descriptor shared by configuration, workspace preparation, application construction,
/// provider-context projection, and handoff delivery.</summary>
public sealed record SquadMemberDefinition(
    SquadMemberId Id,
    string DisplayName,
    RoleId Role,
    string WorktreeName,
    string WorktreePath,
    string ReceiveMode,
    AgentSettings Agent);
