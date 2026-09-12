namespace squad.Domain;

/// <summary>The immutable, ordered roster of one configured squad: its resolved members in configured order and
/// the leader's member identity.</summary>
public sealed record SquadDefinition
{
    public IReadOnlyList<SquadMemberDefinition> Members { get; init; }
    public SquadMemberId Leader { get; init; }

    public SquadDefinition(IReadOnlyList<SquadMemberDefinition> Members, SquadMemberId Leader)
    {
        Contract.Requires(Members.Count > 0, "A squad definition must have at least one member.");
        Contract.Requires(
            Members.Select(member => member.Id).Distinct().Count() == Members.Count,
            "Member identities must be unique.");
        Contract.Requires(
            Members.Any(member => member.Id == Leader),
            "The leader must be a member of the roster.");
        this.Members = Members;
        this.Leader = Leader;
    }
}
