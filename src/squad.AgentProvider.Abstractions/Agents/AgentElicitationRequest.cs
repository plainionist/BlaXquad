using System.Text.Json;

namespace squad.AgentProvider.Abstractions.Agents;

public sealed record AgentElicitationRequest(DateTimeOffset OccurredAt, string RequestId, string Role, string Prompt, string Mode, JsonElement? RequestedSchema = null, string? Url = null) : AgentEvent(OccurredAt);




