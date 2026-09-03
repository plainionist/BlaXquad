namespace squad.Core;

public sealed record RoleTranscriptPage(
    string Role,
    IReadOnlyList<IndexedTranscriptEntry> Entries,
    bool HasMore,
    bool HistoryTruncated);



