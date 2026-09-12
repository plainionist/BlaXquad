namespace squad.AgentProvider.Abstractions;

/// <summary>The opaque identity of one tool call, correlating its start, progress, output, and completion events
/// across a member's session. A sealed reference type, not a struct, so no default-constructed instance can ever
/// bypass its constructor invariant. Rejects a missing or blank value at construction; does not invent a
/// provider-specific format.</summary>
public sealed record ToolCallId
{
    public string Value { get; }

    public ToolCallId(string value)
    {
        Contract.Requires(!string.IsNullOrWhiteSpace(value), "value must not be null or blank.");
        Value = value;
    }

    public override string ToString() => Value;
}
