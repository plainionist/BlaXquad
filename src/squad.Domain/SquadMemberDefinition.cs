namespace squad.Domain;

/// <summary>One immutable, resolved description of a configured squad member: its own operational identity, the
/// reusable role it references, its resolved worktree, receive mode, and normalized agent settings. This is the
/// single canonical member descriptor shared by configuration, workspace preparation, application construction,
/// provider-context projection, and handoff delivery. It is produced only from the validated launch configuration,
/// which always resolves a receive mode; the lenient command-side reader addresses malformed or unresolved
/// receive-mode tokens through its own narrow configuration result instead. It carries only the resolved worktree
/// path: which configured worktree target it came from is workspace-preparation concern, not a runtime concern.</summary>
public sealed record SquadMemberDefinition(
    SquadMemberId Id,
    string DisplayName,
    RoleId Role,
    string WorktreePath,
    ReceiveMode ReceiveMode,
    AgentSettings Agent);
