namespace squad.Ui.Protocol;

/// <summary>Tracks separate transcript-rendering and accessibility-announcement cursors for one role.</summary>
internal sealed record TranscriptSynchronizationPosition(
    long VisualSequence,
    long AnnouncementSequence);
