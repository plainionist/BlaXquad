using System.Text.Json.Serialization;

namespace squad.Core;

internal sealed class SquadConfigurationAgentDocument
{
    [JsonPropertyName("backend")] public string? Backend { get; init; }
    [JsonPropertyName("permissions")] public string? Permissions { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("effort")] public string? Effort { get; init; }
}



