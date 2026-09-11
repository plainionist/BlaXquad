namespace squad.Application.Members;

/// <summary>Captures one member's state at a single synchronization boundary for snapshot publication.</summary>
internal sealed record MemberSnapshot(
    string Id,
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
