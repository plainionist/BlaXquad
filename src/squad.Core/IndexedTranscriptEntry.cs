namespace squad.Core;

public sealed record IndexedTranscriptEntry(
    int EntryIndex,
    TranscriptEntry Entry,
    bool HasArchivedContent = false,
    long ContentStart = 0);



