namespace squad.Application;

/// <summary>Captures one role's state at a single synchronization boundary for snapshot publication.</summary>
internal sealed record AgentRoleSnapshot(
    string Role,
    string Status,
    DateTimeOffset? LastEventAt,
    string? Error,
    string? ActiveTool,
    bool IsWorking,
    string? Model,
    string? Effort,
    decimal? AicUsed,
    long? ContextUsedTokens,
    long? ContextLimitTokens,
    int EventCount);


