namespace squad.Domain;

/// <summary>The distinct operational identity of one configured squad member, unique even when two members share
/// one <see cref="RoleId"/>. A sealed reference type, not a struct, so no default-constructed instance can ever
/// bypass its constructor invariant. Rejects a missing or blank value at construction.</summary>
public sealed record SquadMemberId
{
    public string Value { get; }

    public SquadMemberId(string value)
    {
        Contract.Requires(!string.IsNullOrWhiteSpace(value), "value must not be null or blank.");
        Value = value;
    }

    public override string ToString() => Value;
}
