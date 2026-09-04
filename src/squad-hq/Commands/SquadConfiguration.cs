namespace squad_hq.Commands;

internal sealed record SquadConfiguration(IReadOnlyList<SquadRoleConfiguration> Roles, IReadOnlyList<string> SharedWorktreePaths);



