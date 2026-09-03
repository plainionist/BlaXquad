namespace squad.Core;

public sealed record SquadConfiguration(IReadOnlyList<SquadRoleConfiguration> Roles, IReadOnlyList<string> SharedWorktreePaths);



