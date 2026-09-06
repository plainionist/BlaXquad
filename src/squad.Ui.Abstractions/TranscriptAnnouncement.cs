namespace squad.Ui.Abstractions;

/// <summary>Provides a bounded transcript fragment retained for UI accessibility announcements and recovery.</summary>
public sealed record TranscriptAnnouncement(
    int EntryIndex,
    TranscriptAnnouncementKind Kind,
    string Content,
    bool Truncated = false);


