using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>Pairs a retained announcement fragment with its transcript update sequence for ordered replay.</summary>
internal sealed record SequencedTranscriptAnnouncement(
    long Sequence,
    TranscriptAnnouncement Announcement);


