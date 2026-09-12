namespace squad.AgentProvider.Fake.Control;

/// <summary>One role's observation state within an <see cref="ObservationJournal"/>: its currently active session
/// id, its latest reported prompt, and one <see cref="ObservationState"/> per distinct generic observation kind.
/// The active session id is set only when a session starts and is deliberately never cleared on disposal, so a
/// disposed session remains this role's latest observed session.</summary>
internal sealed class MemberObservationJournal
{
    internal string? ActiveSessionId { get; set; }
    internal string? LatestPrompt { get; set; }
    internal Dictionary<string, ObservationState> Observations { get; } = [];
}
