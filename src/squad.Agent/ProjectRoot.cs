namespace squad.Agent;

/// <summary>Locates the squad project root and current worktree root.</summary>
public static class ProjectRoot
{
    private const string myNotFoundMessage = "Cannot find squad project root";

    /// <summary>Resolves the current worktree's top-level root via git.</summary>
    public static string ResolveViaGit()
    {
        var gitRoot = GitRevParse("--show-toplevel");
        if (gitRoot is not null)
        {
            if (HasSquadConfig(gitRoot))
                return gitRoot;

            var common = GitCommonDir();
            if (common is not null)
            {
                var candidate = Path.GetDirectoryName(common)!;
                if (HasSquadConfig(candidate))
                    return gitRoot;
            }
        }

        throw new CliExitException(1, myNotFoundMessage);
    }

    /// <summary>Resolves the canonical repository root containing blaxquad/squad.json.</summary>
    public static string ResolveProjectRoot(string worktreeRoot)
    {
        var common = GitCommonDir(worktreeRoot);
        if (common is not null)
        {
            var candidate = Path.GetDirectoryName(common)!;
            if (HasSquadConfig(candidate))
                return candidate;
        }

        if (HasSquadConfig(worktreeRoot))
            return worktreeRoot;

        throw new CliExitException(1, myNotFoundMessage);
    }

    private static bool HasSquadConfig(string root) => File.Exists(Path.Combine(root, "blaxquad", "squad.json"));

    private static string? GitRevParse(string arg, string? workingDir = null)
    {
        var result = ProcessRunner.Run("git", workingDir is not null ? ["-C", workingDir, "rev-parse", arg] : ["rev-parse", arg]);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }

    private static string? GitCommonDir(string? workingDir = null)
    {
        var path = GitRevParse("--git-common-dir", workingDir);
        if (path is null)
            return null;
        if (!Path.IsPathRooted(path))
            path = workingDir is not null ? Path.GetFullPath(path, workingDir) : Path.GetFullPath(path);
        return path;
    }
}



