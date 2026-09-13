namespace squad.Ui.Abstractions;

/// <summary>Pairs an entry with its stable history index and the offset of any content retained only in the archive.</summary>
public sealed record IndexedTranscriptEntry(
    int EntryIndex,
    TranscriptEntry Entry,
    bool HasArchivedContent = false,
    long ContentStart = 0);
