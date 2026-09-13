using System.Text.Json.Serialization;

namespace squad.Configuration;

internal sealed class SquadConfigurationMemberDocument
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
    [JsonPropertyName("role")] public string? Role { get; init; }
    [JsonPropertyName("worktree")] public string? Worktree { get; init; }
    [JsonPropertyName("receiveMode")] public string? ReceiveMode { get; init; }
    [JsonPropertyName("agent")] public SquadConfigurationAgentDocument? Agent { get; init; }
}
