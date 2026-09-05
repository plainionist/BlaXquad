using squad.Process;

namespace squad.Configuration;

public static class CurrentRoleResolver
{
    public static RoleRow Resolve(IReadOnlyList<RoleRow> roles, string projectRoot)
    {
        var currentRoot = Normalize(projectRoot);
        var matches = roles.Where(role => Normalize(role.WorktreePath) == currentRoot).ToList();
        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
            throw new CliExitException(1, $"Ambiguous current worktree matches roles: {string.Join(", ", matches.Select(role => role.Role))}");

        throw new CliExitException(1, "Could not resolve the current role from its worktree.");
    }

    private static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }
}



