namespace squad.Ui.Abstractions;

/// <summary>Represents a bounded page of role history and whether older or permanently truncated history remains.</summary>
public sealed record RoleTranscriptPage(
    string Role,
    IReadOnlyList<IndexedTranscriptEntry> Entries,
    bool HasMore,
    bool HistoryTruncated);


