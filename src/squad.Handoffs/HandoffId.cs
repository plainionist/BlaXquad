namespace squad.Handoffs;

/// <summary>The distinct identity of one handoff document, generated once at creation and carried unchanged
/// through validation and delivery.</summary>
public readonly record struct HandoffId(string Value)
{
    public override string ToString() => Value;
}
