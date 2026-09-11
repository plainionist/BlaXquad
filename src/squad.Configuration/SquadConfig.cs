using System.Text.Json;

namespace squad.Configuration;

/// <summary>
/// Provides lenient command-side role lookup from <c>blaxquad/squad.json</c>'s schema-version-2 "members" array.
/// Missing or malformed configuration is represented as an empty role list so individual commands can report
/// context-specific errors. Each row still addresses one configured member by the identity CLI and handoff
/// commands have always used; a genuine role-vs-member distinction is exposed by issue 024 slice 2.
/// </summary>
public static class SquadConfig
{
    /// <summary>Reads resolved role rows, returning an empty list when the configuration cannot be consumed.</summary>
    public static IReadOnlyList<RoleRow> ReadRoles(string projectRoot)
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

            var list = new List<RoleRow>();
            foreach (var memberElem in membersElement.EnumerateArray())
            {
                var name = memberElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var worktree = memberElem.TryGetProperty("worktree", out var w) ? w.GetString() ?? "" : "";
                var receiveMode = memberElem.TryGetProperty("receiveMode", out var r) ? r.GetString() ?? "task" : "task";
                var displayName = memberElem.TryGetProperty("displayName", out var d) ? d.GetString() ?? name : name;

                var worktreePath = worktree == "master"
                    ? projectRoot
                    : Path.Combine(projectRoot, ".worktrees", worktree);

                list.Add(new RoleRow(name, worktree, worktreePath, displayName, receiveMode));
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


