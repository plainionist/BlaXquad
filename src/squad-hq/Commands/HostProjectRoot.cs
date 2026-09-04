using global::squad.Agent.Cli;
using global::squad.Agent.Process;
namespace squad_hq.Commands;

/// <summary>Resolves the main checkout that owns the live squad host.</summary>
public static class HostProjectRoot
{
    public static string ResolveViaGit()
    {
        var gitRoot = GitRevParse("--show-toplevel");
        if (gitRoot is null)
            throw new CliExitException(1, "Cannot find squad project root");

        var common = GitRevParse("--git-common-dir");
        if (common is not null)
        {
            var commonPath = Path.IsPathRooted(common) ? common : Path.GetFullPath(common);
            var candidate = Path.GetDirectoryName(commonPath)!;
            if (File.Exists(Path.Combine(candidate, "blaxquad", "squad.json")))
                return candidate;
        }

        if (File.Exists(Path.Combine(gitRoot, "blaxquad", "squad.json")))
            return gitRoot;

        throw new CliExitException(1, "Cannot find squad project root");
    }

    private static string? GitRevParse(string argument)
    {
        var result = ProcessRunner.Run("git", ["rev-parse", argument]);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }
}



