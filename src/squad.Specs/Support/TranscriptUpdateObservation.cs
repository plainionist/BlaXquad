namespace squad.Specs.Support;

/// <summary>Test-owned, decoded shape of one "transcript.update" message - the dashboard protocol's
/// <c>sequence</c>, <c>operation</c>, <c>entryIndex</c>, and (for an appended or replaced entry) <c>source</c> and
/// <c>content</c> fields, kept behind <see cref="HeadlessUiClient"/> so step definitions never parse the raw wire
/// payload themselves. <see cref="Source"/> and <see cref="Content"/> are null for an "append-content" operation,
/// which carries only <see cref="Content"/> as appended text with no entry of its own.</summary>
public sealed record TranscriptUpdateObservation(
    string Role, long Sequence, string Operation, int EntryIndex, string? Source, string? Content);
