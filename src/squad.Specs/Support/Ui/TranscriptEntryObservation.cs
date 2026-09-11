namespace squad.Specs.Support.Ui;

/// <summary>Test-owned, decoded shape of one entry inside a "transcript.synchronize" message - the dashboard
/// protocol's <c>entryIndex</c>, <c>source</c>, and <c>content</c> fields, kept behind <see cref="HeadlessUiClient"/>
/// so step definitions never parse the raw wire payload themselves.</summary>
public sealed record TranscriptEntryObservation(int EntryIndex, string Source, string Content);
