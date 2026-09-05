using System.Text.Json.Serialization;

namespace squad.Configuration;

internal sealed class SquadConfigurationAgentDocument
{
    [JsonPropertyName("permissions")] public string? Permissions { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("effort")] public string? Effort { get; init; }
}



