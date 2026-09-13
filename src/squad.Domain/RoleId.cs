namespace squad.Domain;

/// <summary>The identity of one reusable role definition. Multiple squad members may reference the same role. A
/// sealed reference type, not a struct, so no default-constructed instance can ever bypass its constructor
/// invariant. Rejects a missing or blank value at construction.</summary>
public sealed record RoleId
{
    public string Value { get; }

    public RoleId(string value)
    {
        Contract.Requires(!string.IsNullOrWhiteSpace(value), "value must not be null or blank.");

        Value = value;
    }

    public override string ToString() => Value;
}
