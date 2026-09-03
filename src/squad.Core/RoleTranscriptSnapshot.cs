namespace squad.Core;

public sealed record RoleTranscriptSnapshot(
    string Role,
    long Sequence,
    IReadOnlyList<IndexedTranscriptEntry> Entries,
    bool HasMore,
    bool HistoryTruncated);



