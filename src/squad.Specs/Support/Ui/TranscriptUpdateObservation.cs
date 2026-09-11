namespace squad.Specs.Support.Ui;

/// <summary>Test-owned, decoded shape of one "transcript.update" message - the dashboard protocol's
/// <c>sequence</c>, <c>operation</c>, <c>entryIndex</c>, and (for an appended or replaced entry) <c>source</c> and
/// <c>content</c> fields, kept behind <see cref="HeadlessUiClient"/> so step definitions never parse the raw wire
/// payload themselves. <see cref="Source"/> and <see cref="Content"/> are null for an "append-content" operation,
/// which carries only <see cref="Content"/> as appended text with no entry of its own. <see cref="HasArchivedContent"/>
/// and <see cref="ContentStart"/> report whether the entry's in-memory retention bound trimmed its content (with the
/// full content still available through the archive), and <see cref="AnnouncementTruncated"/> and
/// <see cref="AnnouncementContentLength"/> report the recovery announcement's own independent bound.</summary>
public sealed record TranscriptUpdateObservation(
    string Role,
    long Sequence,
    string Operation,
    int EntryIndex,
    string? Source,
    string? Content,
    bool HasArchivedContent = false,
    long ContentStart = 0,
    bool AnnouncementTruncated = false,
    int? AnnouncementContentLength = null);
