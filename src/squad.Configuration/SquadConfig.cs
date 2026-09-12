using System.Text.Json;

using squad.Domain;

namespace squad.Configuration;

/// <summary>
/// Provides lenient command-side member lookup from <c>blaxquad/squad.json</c>'s schema-version-2 "members"
/// array. Missing or malformed configuration is represented as an empty member list so individual commands can
/// report context-specific errors. Each configured participant is addressed by the member identity CLI and
/// handoff commands have always used, distinct from its (possibly shared) role. An omitted receive mode defaults
/// to <see cref="ReceiveMode.Task"/>; an explicitly empty or unsupported receive mode maps to <c>null</c> so a
/// command can still report it, without inventing an "unknown" domain value or defaulting it to task.
/// </summary>
public static class SquadConfig
{
    /// <summary>Reads resolved members, returning an empty list when the configuration cannot be consumed.</summary>
    public static IReadOnlyList<SquadMemberDefinition> ReadMembers(string projectRoot)
    {
        var configFile = Path.Combine(projectRoot, "blaxquad", "squad.json");
        if (!File.Exists(configFile))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(configFile);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("members", out var membersElement) || membersElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<SquadMemberDefinition>();
            foreach (var memberElem in membersElement.EnumerateArray())
            {
                var name = memberElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var role = memberElem.TryGetProperty("role", out var ro) ? ro.GetString() ?? name : name;
                var worktree = memberElem.TryGetProperty("worktree", out var w) ? w.GetString() ?? "" : "";
                var rawReceiveMode = memberElem.TryGetProperty("receiveMode", out var r) ? r.GetString() ?? "task" : "task";
                var receiveMode = rawReceiveMode switch
                {
                    "task" => ReceiveMode.Task,
                    "batch" => ReceiveMode.Batch,
                    _ => (ReceiveMode?)null,
                };
                var displayName = memberElem.TryGetProperty("displayName", out var d) ? d.GetString() ?? name : name;

                var permissions = "prompt";
                string? model = null;
                string? effort = null;
                if (memberElem.TryGetProperty("agent", out var agentElem) && agentElem.ValueKind == JsonValueKind.Object)
                {
                    permissions = agentElem.TryGetProperty("permissions", out var p) ? p.GetString() ?? "prompt" : "prompt";
                    model = agentElem.TryGetProperty("model", out var m) ? m.GetString() : null;
                    effort = agentElem.TryGetProperty("effort", out var e) ? e.GetString() : null;
                }

                var worktreePath = worktree == "master"
                    ? projectRoot
                    : Path.Combine(projectRoot, ".worktrees", worktree);

                list.Add(new SquadMemberDefinition(
                    new SquadMemberId(name),
                    displayName,
                    new RoleId(role),
                    worktree,
                    worktreePath,
                    receiveMode,
                    new AgentSettings(permissions, model, effort)));
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    public static bool MemberKnown(IEnumerable<SquadMemberDefinition> members, string name) =>
        members.Any(member => member.Id.Value == name);

    public static SquadMemberDefinition? Find(IEnumerable<SquadMemberDefinition> members, string name) =>
        members.FirstOrDefault(member => member.Id.Value == name);
}


