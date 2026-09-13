namespace squad.AgentProvider.Abstractions;

/// <summary>Captures the process environment and role definitions used to create one runtime generation.</summary>
public sealed record AgentBackendContext(
    string WorkingDirectory,
    string ScriptDirectory,
    IReadOnlyList<AgentRoleContext> Roles,
    IReadOnlyDictionary<string, string> Environment);
