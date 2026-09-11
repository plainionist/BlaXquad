namespace squad.Specs.Support.Ui;

/// <summary>Test-owned, decoded shape of one "transcript.synchronize" message for one role - the dashboard
/// protocol's <c>sequence</c> field alongside its ordered, indexed <see cref="TranscriptEntryObservation"/>
/// entries.</summary>
public sealed record TranscriptSynchronizationObservation(
    string Role, long Sequence, IReadOnlyList<TranscriptEntryObservation> Entries);
