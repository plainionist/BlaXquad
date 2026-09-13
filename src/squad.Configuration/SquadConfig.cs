using System.Text.Json;

using squad.Domain;

namespace squad.Configuration;

/// <summary>
/// Provides lenient command-side member lookup from <c>blaxquad/squad.json</c>'s schema-version-2 "members"
/// array. Missing or malformed configuration is represented as an empty member list so individual commands can
/// report context-specific errors. Each configured participant is addressed by the member identity CLI and
/// handoff commands have always used, distinct from its (possibly shared) role. An omitted receive mode defaults
/// to <see cref="ReceiveMode.Task"/>; an explicitly empty or unsupported receive mode is represented on
/// <see cref="SquadConfigMember"/> as a <c>null</c> parsed <see cref="ReceiveMode"/> alongside its raw configured
/// token, so a command can still distinguish an empty token from an unsupported one for its existing diagnostics
/// without a second scan of the configuration file.
/// </summary>
public static class SquadConfig
{
    /// <summary>Reads resolved members, returning an empty list when the configuration cannot be consumed.</summary>
    public static IReadOnlyList<SquadConfigMember> ReadMembers(string projectRoot)
    {
        var list = new List<SquadConfigMember>();

        foreach (var memberElem in EnumerateMemberElements(projectRoot))
        {
            var name = memberElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var worktree = memberElem.TryGetProperty("worktree", out var w) ? w.GetString() ?? "" : "";
            var rawReceiveMode = RawReceiveMode(memberElem);
            var receiveMode = rawReceiveMode switch
            {
                "task" => ReceiveMode.Task,
                "batch" => ReceiveMode.Batch,
                _ => (ReceiveMode?)null,
            };

            var worktreePath = worktree == "master"
                ? projectRoot
                : Path.Combine(projectRoot, ".worktrees", worktree);

            list.Add(new SquadConfigMember(new SquadMemberId(name), worktreePath, receiveMode, rawReceiveMode));
        }

        return list;
    }

    public static bool MemberKnown(IEnumerable<SquadConfigMember> members, string name) =>
        members.Any(member => member.Id.Value == name);

    public static SquadConfigMember? Find(IEnumerable<SquadConfigMember> members, string name) =>
        members.FirstOrDefault(member => member.Id.Value == name);

    static string RawReceiveMode(JsonElement memberElem) =>
        memberElem.TryGetProperty("receiveMode", out var r) ? r.GetString() ?? "task" : "task";

    static List<JsonElement> EnumerateMemberElements(string projectRoot)
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

            return [.. membersElement.EnumerateArray().Select(memberElem => memberElem.Clone())];
        }
        catch
        {
            return [];
        }
    }
}
