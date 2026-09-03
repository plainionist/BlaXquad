namespace squad.Abstractions;

public sealed record AgentBackendContext(
    string WorkingDirectory,
    string ScriptDirectory,
    IReadOnlyList<AgentRoleContext> Roles,
    IReadOnlyDictionary<string, string> Environment);



