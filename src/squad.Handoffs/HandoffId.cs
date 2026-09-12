namespace squad.Handoffs;

/// <summary>The distinct identity of one handoff document, generated once at creation and carried unchanged
/// through validation and delivery. Rejects a missing or blank value at construction so no invalid instance can
/// ever exist.</summary>
public readonly record struct HandoffId
{
    public string Value { get; }

    public HandoffId(string value)
    {
        Contract.Requires(!string.IsNullOrWhiteSpace(value), "value must not be null or blank.");
        Value = value;
    }

    public override string ToString() => Value;
}
