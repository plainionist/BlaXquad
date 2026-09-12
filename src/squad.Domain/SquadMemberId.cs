namespace squad.Domain;

/// <summary>The distinct operational identity of one configured squad member, unique even when two members share
/// one <see cref="RoleId"/>.</summary>
public readonly record struct SquadMemberId(string Value)
{
    public override string ToString() => Value;
}
