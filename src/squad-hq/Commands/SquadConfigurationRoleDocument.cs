using System.Text.Json.Serialization;

namespace squadHQ.Commands;

internal sealed class SquadConfigurationRoleDocument
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("worktree")] public string? Worktree { get; init; }
    [JsonPropertyName("receiveMode")] public string? ReceiveMode { get; init; }
    [JsonPropertyName("agent")] public SquadConfigurationAgentDocument? Agent { get; init; }
}



