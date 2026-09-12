namespace squad.Domain;

/// <summary>The immutable, ordered roster of one configured squad: its resolved members in configured order and
/// the leader's member identity.</summary>
public sealed record SquadDefinition(
    IReadOnlyList<SquadMemberDefinition> Members,
    SquadMemberId Leader);
