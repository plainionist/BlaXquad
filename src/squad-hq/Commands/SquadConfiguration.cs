namespace squadHQ.Commands;

internal sealed record SquadConfiguration(IReadOnlyList<SquadRoleConfiguration> Roles, IReadOnlyList<string> SharedWorktreePaths);



