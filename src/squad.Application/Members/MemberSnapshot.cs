using squad.AgentProvider.Abstractions.Agents;
using squad.Domain;

namespace squad.Application.Members;

/// <summary>
/// Captures one member's configured identity, presentation, projected status, pending interactions, and transcript
/// position at a single synchronization boundary for immutable snapshot publication.
/// </summary>
internal sealed record MemberSnapshot(
    string Id,
    string DisplayName,
    string Role,
    SquadMemberStatus Status,
    DateTimeOffset? LastEventAt,
    string? Error,
    string? ActiveTool,
    bool IsWorking,
    string? Model,
    string? Effort,
    decimal? AicUsed,
    long? ContextUsedTokens,
    long? ContextLimitTokens,
    int EventCount,
    long TranscriptPosition,
    IReadOnlyCollection<AgentPermissionRequest> Permissions,
    IReadOnlyCollection<AgentInputRequest> Inputs,
    IReadOnlyCollection<AgentElicitationRequest> Elicitations);
