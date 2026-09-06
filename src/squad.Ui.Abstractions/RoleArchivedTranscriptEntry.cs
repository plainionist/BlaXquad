namespace squad.Ui.Abstractions;

/// <summary>Reports an archived entry together with metadata needed to reconstruct truncated retained content.</summary>
public sealed record RoleArchivedTranscriptEntry(
    string Role,
    long Sequence,
    int EntryIndex,
    TranscriptEntry? Entry,
    bool ContentTruncated,
    long TotalContentCharacters,
    int ArchivedPrefixCharacters);


