namespace squad.Domain;

/// <summary>A squad member's projected lifecycle status: <see cref="Starting"/> before its first provider event,
/// <see cref="Running"/> once its session has started, <see cref="Idle"/> when it is admitting new work,
/// <see cref="Stopped"/> or <see cref="Error"/> once it has terminated. The stable lowercase protocol spellings
/// ("starting"/"running"/"idle"/"stopped"/"error") are mapped to and from this enum at the application/presentation
/// boundary that composes <c>state.snapshot</c>.</summary>
public enum SquadMemberStatus
{
    Starting,
    Running,
    Idle,
    Stopped,
    Error
}
