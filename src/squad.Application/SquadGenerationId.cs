namespace squad.Application;

/// <summary>
/// The strong identity of one replaceable squad generation. Every command, provider event, readiness observation,
/// external-operation completion, transcript mutation, and handoff wake-up either carries this identity or is
/// captured through an object that is bound to it, so a result produced for a retired generation can never mutate
/// or publish through its replacement.
/// </summary>
public readonly record struct SquadGenerationId(Guid Value)
{
    public static SquadGenerationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
