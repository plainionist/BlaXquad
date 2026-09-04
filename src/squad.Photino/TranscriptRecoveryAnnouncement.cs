
namespace squad.Photino;

public sealed record TranscriptRecoveryAnnouncement(
    long AfterSequence,
    long ThroughSequence,
    IReadOnlyList<SequencedTranscriptAnnouncement> Fragments,
    bool Truncated);



