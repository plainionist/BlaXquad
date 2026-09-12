namespace squad.Ui.Abstractions;

/// <summary>The closed set of transcript entry origins - the complete current vocabulary interpreted by the UI and
/// persisted in transcript archives.</summary>
public enum TranscriptSource
{
    Harness,
    User,
    Assistant,
    Reasoning,
    System,
    Error,
    Tool,
    Read,
    Subagent,
}
