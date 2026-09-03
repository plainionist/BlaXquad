using System.Text.Json;

namespace squad.Agent;

/// <summary>Reads role configurations from blaxquad/squad.json.</summary>
public static class SquadConfig
{
    public static IReadOnlyList<RoleRow> ReadRoles(string projectRoot)
    {
        var configFile = Path.Combine(projectRoot, "blaxquad", "squad.json");
        if (!File.Exists(configFile))
            return [];

        try
        {
            using var stream = File.OpenRead(configFile);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("roles", out var rolesElement) || rolesElement.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<RoleRow>();
            foreach (var roleElem in rolesElement.EnumerateArray())
            {
                var name = roleElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var worktree = roleElem.TryGetProperty("worktree", out var w) ? w.GetString() ?? "" : "";
                var receiveMode = roleElem.TryGetProperty("receiveMode", out var r) ? r.GetString() ?? "task" : "task";
                var displayName = roleElem.TryGetProperty("displayName", out var d) ? d.GetString() ?? name : name;
                var agent = "copilot";
                if (roleElem.TryGetProperty("agent", out var agentElem) && agentElem.ValueKind == JsonValueKind.Object)
                {
                    if (agentElem.TryGetProperty("backend", out var b))
                        agent = b.GetString() ?? "copilot";
                }

                var worktreePath = worktree == "master"
                    ? projectRoot
                    : Path.Combine(projectRoot, ".worktrees", worktree);

                list.Add(new RoleRow(name, worktree, worktreePath, displayName, agent, receiveMode));
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    public static bool RoleKnown(IEnumerable<RoleRow> rows, string role) =>
        rows.Any(r => r.Role == role);

    public static RoleRow? Find(IEnumerable<RoleRow> rows, string role) =>
        rows.FirstOrDefault(r => r.Role == role);
}



