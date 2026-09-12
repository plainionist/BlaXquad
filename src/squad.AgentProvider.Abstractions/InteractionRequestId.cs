namespace squad.AgentProvider.Abstractions;

/// <summary>The opaque identity of one pending permission, input, or elicitation interaction request - distinct
/// across all three interaction kinds even when their underlying provider or wire values happen to coincide.</summary>
public readonly record struct InteractionRequestId(string Value)
{
    public override string ToString() => Value;
}
