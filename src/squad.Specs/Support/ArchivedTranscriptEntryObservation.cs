namespace squad.Specs.Support;

/// <summary>Test-owned, decoded shape of one "transcript.entry" message for one role and entry index - the
/// dashboard protocol's authoritative answer for whether that entry's content is still available (evicted from
/// live retention but preserved in the archive, or rotated out of the archive entirely), never the archive's own
/// on-disk storage coordinates.</summary>
public sealed record ArchivedTranscriptEntryObservation(
    string Role,
    long Sequence,
    int EntryIndex,
    string? Content,
    bool ContentTruncated,
    long TotalContentCharacters,
    long ArchivedPrefixCharacters);
