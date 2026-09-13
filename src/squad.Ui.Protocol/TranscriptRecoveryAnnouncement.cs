
namespace squad.Ui.Protocol;

/// <summary>
/// Describes the retained announcement fragments for a requested replay interval. <c>Truncated</c> indicates that
/// part of the required interval has already fallen outside journal retention.
/// </summary>
internal sealed record TranscriptRecoveryAnnouncement(
    long AfterSequence,
    long ThroughSequence,
    IReadOnlyList<SequencedTranscriptAnnouncement> Fragments,
    bool Truncated);
