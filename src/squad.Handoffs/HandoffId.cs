namespace squad.Handoffs;

/// <summary>The distinct identity of one handoff document, generated once at creation and carried unchanged
/// through validation and delivery. A sealed reference type, not a struct, so no default-constructed instance can
/// ever bypass its constructor invariant. Rejects a missing or blank value at construction.</summary>
public sealed record HandoffId
{
    public string Value { get; }

    public HandoffId(string value)
    {
        Contract.Requires(!string.IsNullOrWhiteSpace(value), "value must not be null or blank.");
        Value = value;
    }

    public override string ToString() => Value;
}