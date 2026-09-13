namespace squad.Ui.Abstractions;

public sealed record TranscriptEntry(DateTimeOffset OccurredAt, TranscriptSource Source, string Content);
