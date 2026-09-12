using squad.Domain;

namespace squad.Ui.Abstractions;

/// <summary>Captures a role's newest retained entries at a transcript sequence boundary.</summary>
public sealed record RoleTranscriptSnapshot(
    SquadMemberId MemberId,
    long Sequence,
    IReadOnlyList<IndexedTranscriptEntry> Entries,
    bool HasMore,
    bool HistoryTruncated);


