namespace squad.Configuration;

public sealed record SquadConfiguration(IReadOnlyList<SquadRoleConfiguration> Roles, string Leader, IReadOnlyList<string> SharedWorktreePaths);



