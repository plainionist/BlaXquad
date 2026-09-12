using squad.Domain;

namespace squad.Ui.Abstractions;

/// <summary>Reports an archived entry together with metadata needed to reconstruct truncated retained content.</summary>
public sealed record RoleArchivedTranscriptEntry(
    SquadMemberId MemberId,
    long Sequence,
    int EntryIndex,
    TranscriptEntry? Entry,
    bool ContentTruncated,
    long TotalContentCharacters,
    int ArchivedPrefixCharacters);


