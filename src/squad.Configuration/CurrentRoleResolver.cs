using squad.Process;

namespace squad.Configuration;

/// <summary>Matches a normalized worktree path to exactly one configured member, the participant that CLI and
/// handoff commands address as the "current role".</summary>
public static class CurrentRoleResolver
{
    /// <summary>Fails with a controlled CLI error when no member or multiple members match the supplied path.</summary>
    public static SquadConfigMember Resolve(IReadOnlyList<SquadConfigMember> members, string projectRoot)
    {
        var currentRoot = Normalize(projectRoot);
        var matches = members.Where(member => Normalize(member.WorktreePath) == currentRoot).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new CliExitException(1, $"Ambiguous current worktree matches roles: {string.Join(", ", matches.Select(member => member.Id.Value))}");
        }

        throw new CliExitException(1, "Could not resolve the current role from its worktree.");
    }

    private static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }
}


