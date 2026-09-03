namespace squad.Core;

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



