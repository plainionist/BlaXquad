namespace squad.Configuration;

public sealed record SquadConfiguration(IReadOnlyList<SquadRoleConfiguration> Roles, IReadOnlyList<string> SharedWorktreePaths);



