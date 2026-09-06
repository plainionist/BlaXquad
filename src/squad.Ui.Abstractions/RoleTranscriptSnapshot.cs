namespace squad.Ui.Abstractions;

/// <summary>Captures a role's newest retained entries at a transcript sequence boundary.</summary>
public sealed record RoleTranscriptSnapshot(
    string Role,
    long Sequence,
    IReadOnlyList<IndexedTranscriptEntry> Entries,
    bool HasMore,
    bool HistoryTruncated);


