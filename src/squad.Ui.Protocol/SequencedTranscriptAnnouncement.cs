using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

internal sealed record SequencedTranscriptAnnouncement(
    long Sequence,
    TranscriptAnnouncement Announcement);



