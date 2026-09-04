namespace squad.Ui.Abstractions;

public sealed record RoleTranscriptPage(
    string Role,
    IReadOnlyList<IndexedTranscriptEntry> Entries,
    bool HasMore,
    bool HistoryTruncated);



