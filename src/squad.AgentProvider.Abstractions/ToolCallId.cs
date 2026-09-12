namespace squad.AgentProvider.Abstractions;

/// <summary>The opaque identity of one tool call, correlating its start, progress, output, and completion events
/// across a member's session.</summary>
public readonly record struct ToolCallId(string Value)
{
    public override string ToString() => Value;
}
