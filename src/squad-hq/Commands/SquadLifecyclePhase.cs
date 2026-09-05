namespace squadHQ.Commands;

/// <summary>
/// The lifecycle phases owned by <see cref="SessionRegistry"/>. There is exactly one phase for the whole squad
/// process: no other type tracks a parallel notion of "started", "stopping", or "accepting".
/// </summary>
internal enum SquadLifecyclePhase
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
}
