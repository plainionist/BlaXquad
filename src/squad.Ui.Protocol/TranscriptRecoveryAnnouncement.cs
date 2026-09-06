
namespace squad.Ui.Protocol;

public sealed record TranscriptRecoveryAnnouncement(
    long AfterSequence,
    long ThroughSequence,
    IReadOnlyList<SequencedTranscriptAnnouncement> Fragments,
    bool Truncated);



