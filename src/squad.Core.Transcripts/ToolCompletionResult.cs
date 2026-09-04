using global::squad.Ui.Abstractions;

namespace squad.Core.Transcripts;

/// <summary>
/// Reports the transcript update (if any) produced by completing a tool call, along with the active tool name that
/// the event projector should assign to the role's active-tool status at the same commit point.
/// </summary>
public sealed record ToolCompletionResult(TranscriptUpdate? Update, string? ActiveTool);
