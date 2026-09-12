namespace squad.AgentProvider.CopilotSdk;

/// <summary>One SDK tool call's diagnostic trace correlation: its reported tool name, if a start event has been
/// seen, and the previous payload content recorded for it, if any. Start and payload events for the same tool
/// call may arrive in either order, so either field may be set before the other.</summary>
internal sealed class ToolTraceState
{
    internal string? ToolName { get; set; }
    internal string? PreviousPayload { get; set; }
}
