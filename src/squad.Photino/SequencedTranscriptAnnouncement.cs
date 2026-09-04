using global::squad.Ui.Abstractions;

namespace squad.Photino;

public sealed record SequencedTranscriptAnnouncement(
    long Sequence,
    TranscriptAnnouncement Announcement);



