namespace squad.AgentProvider.Abstractions;

/// <summary>The opaque identity of one pending permission, input, or elicitation interaction request - distinct
/// across all three interaction kinds even when their underlying provider or wire values happen to coincide. A
/// sealed reference type, not a struct, so no default-constructed instance can ever bypass its constructor
/// invariant. Rejects a missing or blank value at construction; does not invent a provider-specific format.</summary>
public sealed record InteractionRequestId
{
    public string Value { get; }

    public InteractionRequestId(string value)
    {
        Contract.Requires(!string.IsNullOrWhiteSpace(value), "value must not be null or blank.");
        Value = value;
    }

    public override string ToString() => Value;
}
