using System.Text.Json.Serialization;

namespace squad_hq.Commands;

internal sealed class SquadConfigurationDocument
{
    [JsonPropertyName("roles")]
    public List<SquadConfigurationRoleDocument>? Roles { get; init; }

    [JsonPropertyName("sharedWorktreePaths")]
    public List<string>? SharedWorktreePaths { get; init; }
}



