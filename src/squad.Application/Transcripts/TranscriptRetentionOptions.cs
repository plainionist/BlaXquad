namespace squad.Application.Transcripts;

/// <summary>Defines independent size bounds for live transcript state, archived history, and UI announcements.</summary>
internal sealed record TranscriptRetentionOptions
{
    public int MaxRetainedEntries { get; init; }
    public int MaxRetainedContentCharacters { get; init; }
    public int MaxRetainedEntryCharacters { get; init; }
    public int MaxArchivedEntries { get; init; }
    public int MaxArchivedContentCharacters { get; init; }
    public int MaxArchivedEntryCharacters { get; init; }
    public int MaxAnnouncementCharacters { get; init; }

    public TranscriptRetentionOptions(
        int MaxRetainedEntries = 500,
        int MaxRetainedContentCharacters = 1_000_000,
        int MaxRetainedEntryCharacters = 250_000,
        int MaxArchivedEntries = 10_000,
        int MaxArchivedContentCharacters = 20_000_000,
        int MaxArchivedEntryCharacters = 2_000_000,
        int MaxAnnouncementCharacters = 16_384)
    {
        Contract.Requires(MaxRetainedEntries > 0, "MaxRetainedEntries must be positive.");
        Contract.Requires(MaxRetainedContentCharacters > 0, "MaxRetainedContentCharacters must be positive.");
        Contract.Requires(MaxRetainedEntryCharacters > 0, "MaxRetainedEntryCharacters must be positive.");
        Contract.Requires(MaxArchivedEntries > 0, "MaxArchivedEntries must be positive.");
        Contract.Requires(MaxArchivedContentCharacters > 0, "MaxArchivedContentCharacters must be positive.");
        Contract.Requires(MaxArchivedEntryCharacters > 0, "MaxArchivedEntryCharacters must be positive.");
        Contract.Requires(MaxAnnouncementCharacters > 0, "MaxAnnouncementCharacters must be positive.");
        Contract.Requires(
            MaxRetainedEntryCharacters <= MaxRetainedContentCharacters,
            "MaxRetainedEntryCharacters must not exceed MaxRetainedContentCharacters.");
        Contract.Requires(
            MaxArchivedEntryCharacters <= MaxArchivedContentCharacters,
            "MaxArchivedEntryCharacters must not exceed MaxArchivedContentCharacters.");
        this.MaxRetainedEntries = MaxRetainedEntries;
        this.MaxRetainedContentCharacters = MaxRetainedContentCharacters;
        this.MaxRetainedEntryCharacters = MaxRetainedEntryCharacters;
        this.MaxArchivedEntries = MaxArchivedEntries;
        this.MaxArchivedContentCharacters = MaxArchivedContentCharacters;
        this.MaxArchivedEntryCharacters = MaxArchivedEntryCharacters;
        this.MaxAnnouncementCharacters = MaxAnnouncementCharacters;
    }
}


