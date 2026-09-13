namespace squad.Process;

/// <summary>Agent-safe lookup of executables on <c>PATH</c> (and <c>PATHEXT</c> on Windows).</summary>
public static class ExecutableLocator
{
    public static bool Exists(string command) => Resolve(command) is not null;

    /// <summary>Resolves <paramref name="command"/> to its full path - an explicitly qualified path, or the first
    /// match found by searching <c>PATH</c> (and <c>PATHEXT</c> on Windows) - or <see langword="null"/> if it
    /// cannot be resolved. Callers that both probe and later launch a command must use this so both steps agree
    /// on exactly the same file, rather than probing one location and launching whatever <c>PATH</c> search order
    /// a shell-free process start happens to prefer.</summary>
    public static string? Resolve(string command)
    {

        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];
        var directories = Path.IsPathFullyQualified(command) || command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar)
            ? [Path.GetDirectoryName(command) ?? Directory.GetCurrentDirectory()]
            : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path => path.Trim('"'));
        var name = Path.GetFileName(command);

        foreach (var directory in directories)
        {

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, Path.HasExtension(name) ? name : name + extension);

                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

            }

        }

        return null;
    }
}
