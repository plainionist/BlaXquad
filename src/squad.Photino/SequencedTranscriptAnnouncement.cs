using global::squad.Core;

namespace squad.Photino;

internal sealed record SequencedTranscriptAnnouncement(
    long Sequence,
    TranscriptAnnouncement Announcement);



