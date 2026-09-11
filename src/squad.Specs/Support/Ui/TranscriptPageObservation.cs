namespace squad.Specs.Support.Ui;

/// <summary>Test-owned, decoded shape of one "transcript.page" message for one role - the dashboard protocol's
/// ordered, indexed <see cref="TranscriptEntryObservation"/> entries immediately preceding the requested index,
/// alongside whether still-older entries remain available to page back further.</summary>
public sealed record TranscriptPageObservation(
    string Role, IReadOnlyList<TranscriptEntryObservation> Entries, bool HasMore, bool HistoryTruncated);
