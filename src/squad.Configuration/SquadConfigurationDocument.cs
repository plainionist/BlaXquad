using System.Text.Json.Serialization;

namespace squad.Configuration;

internal sealed class SquadConfigurationDocument
{
    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; init; }

    [JsonPropertyName("leader")]
    public string? Leader { get; init; }

    /// <summary>A non-empty catalog of unique, reusable role names. A role owns only its prompt file; per-member
    /// settings live in <see cref="Members"/>.</summary>
    [JsonPropertyName("roles")]
    public List<string>? Roles { get; init; }

    [JsonPropertyName("members")]
    public List<SquadConfigurationMemberDocument>? Members { get; init; }

    [JsonPropertyName("sharedWorktreePaths")]
    public List<string>? SharedWorktreePaths { get; init; }

    [JsonPropertyName("gitHistoryCommand")]
    public List<string>? GitHistoryCommand { get; init; }
}
