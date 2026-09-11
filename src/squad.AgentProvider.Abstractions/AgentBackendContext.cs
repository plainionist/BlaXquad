namespace squad.AgentProvider.Abstractions;

/// <summary>Captures the process environment and member definitions used to create one runtime generation.</summary>
public sealed record AgentBackendContext(
    string WorkingDirectory,
    string ScriptDirectory,
    IReadOnlyList<AgentMemberContext> Members,
    IReadOnlyDictionary<string, string> Environment);


