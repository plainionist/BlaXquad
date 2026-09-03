namespace squad.Core;

public sealed record TranscriptRetentionOptions(
    int MaxRetainedEntries = 500,
    int MaxRetainedContentCharacters = 1_000_000,
    int MaxRetainedEntryCharacters = 250_000,
    int MaxArchivedEntries = 10_000,
    int MaxArchivedContentCharacters = 20_000_000,
    int MaxArchivedEntryCharacters = 2_000_000,
    int MaxAnnouncementCharacters = 16_384);



