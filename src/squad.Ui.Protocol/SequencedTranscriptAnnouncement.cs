using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>Pairs a retained announcement fragment with its transcript update sequence for ordered replay.</summary>
public sealed record SequencedTranscriptAnnouncement(
    long Sequence,
    TranscriptAnnouncement Announcement);


