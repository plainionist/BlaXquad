using System.Text.Json;

namespace squad.Abstractions;

public sealed record AgentElicitationResponse(string Action, JsonElement? Content);



