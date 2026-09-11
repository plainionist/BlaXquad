namespace squad.Application;

/// <summary>
/// The currently installed squad generation and its strong identity, captured as one value so a caller that reads
/// the active-squad slot can never pair one generation's identity with another generation's members.
/// </summary>
internal sealed record SquadInstallation(SquadGenerationId Generation, SquadMembers Members);
