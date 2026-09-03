
namespace squad.Photino;

internal sealed record TranscriptRecoveryAnnouncement(
    long AfterSequence,
    long ThroughSequence,
    IReadOnlyList<SequencedTranscriptAnnouncement> Fragments,
    bool Truncated);



