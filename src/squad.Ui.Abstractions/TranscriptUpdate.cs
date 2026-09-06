namespace squad.Ui.Abstractions;

/// <summary>Describes one sequenced mutation of a role transcript and its optional recovery announcement.</summary>
public sealed record TranscriptUpdate(
    string Role,
    long Sequence,
    TranscriptUpdateKind Kind,
    int EntryIndex,
    TranscriptEntry? Entry,
    string? Content,
    bool HasArchivedContent = false,
    long ContentStart = 0,
    TranscriptAnnouncement? Announcement = null);


