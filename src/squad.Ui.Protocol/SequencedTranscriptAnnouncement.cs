using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

public sealed record SequencedTranscriptAnnouncement(
    long Sequence,
    TranscriptAnnouncement Announcement);



