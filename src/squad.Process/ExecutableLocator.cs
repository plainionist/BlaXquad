namespace squad.Process;

/// <summary>Agent-safe lookup of executables on <c>PATH</c> (and <c>PATHEXT</c> on Windows).</summary>
public static class ExecutableLocator
{
    public static bool Exists(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

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

        return directories.Any(directory => extensions.Any(extension =>
            File.Exists(Path.Combine(directory, Path.HasExtension(name) ? name : name + extension))));
    }
}
