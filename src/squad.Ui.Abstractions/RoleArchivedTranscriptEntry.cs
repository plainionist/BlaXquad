namespace squad.Ui.Abstractions;

public sealed record RoleArchivedTranscriptEntry(
    string Role,
    long Sequence,
    int EntryIndex,
    TranscriptEntry? Entry,
    bool ContentTruncated,
    long TotalContentCharacters,
    int ArchivedPrefixCharacters);



