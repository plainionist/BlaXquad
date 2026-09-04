using System.Text.Json;

namespace squad.AgentProvider.Abstractions;

public sealed record AgentElicitationResponse(string Action, JsonElement? Content);



