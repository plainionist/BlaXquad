namespace squad.Ui.Abstractions;

public sealed record TranscriptAnnouncement(
    int EntryIndex,
    TranscriptAnnouncementKind Kind,
    string Content,
    bool Truncated = false);



