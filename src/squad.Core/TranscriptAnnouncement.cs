namespace squad.Core;

public sealed record TranscriptAnnouncement(
    int EntryIndex,
    TranscriptAnnouncementKind Kind,
    string Content,
    bool Truncated = false);



