namespace squad.Domain;

/// <summary>The identity of one reusable role definition. Multiple squad members may reference the same role.</summary>
public readonly record struct RoleId(string Value)
{
    public override string ToString() => Value;
}
