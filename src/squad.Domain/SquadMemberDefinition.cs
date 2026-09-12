namespace squad.Domain;

/// <summary>One immutable, resolved description of a configured squad member: its own operational identity, the
/// reusable role it references, its resolved worktree, receive mode, and normalized agent settings. This is the
/// single canonical member descriptor shared by configuration, workspace preparation, application construction,
/// provider-context projection, and handoff delivery. <see cref="ReceiveMode"/> is nullable because the lenient
/// command-side reader must still address a member whose configured mode did not resolve to <c>Task</c> or
/// <c>Batch</c> (an empty or unsupported value); every member produced by the validated launch configuration
/// always carries a non-null value.</summary>
public sealed record SquadMemberDefinition(
    SquadMemberId Id,
    string DisplayName,
    RoleId Role,
    string WorktreeName,
    string WorktreePath,
    ReceiveMode? ReceiveMode,
    AgentSettings Agent);
