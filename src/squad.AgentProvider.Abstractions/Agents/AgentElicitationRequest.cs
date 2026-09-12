using System.Text.Json;

namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentElicitationRequest(DateTimeOffset OccurredAt, InteractionRequestId RequestId, string Prompt, string Mode, JsonElement? RequestedSchema = null, string? Url = null) : AgentEvent(OccurredAt);




