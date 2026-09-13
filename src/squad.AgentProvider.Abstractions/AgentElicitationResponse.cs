using System.Text.Json;

namespace squad.AgentProvider.Abstractions;

public sealed record AgentElicitationResponse(ElicitationAction Action, JsonElement? Content);
