using global::squad.Ui.Abstractions;

namespace squad.Photino;

internal sealed record SequencedTranscriptAnnouncement(
    long Sequence,
    TranscriptAnnouncement Announcement);



