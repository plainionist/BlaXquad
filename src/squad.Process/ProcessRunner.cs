using System.Diagnostics;

namespace squad.Process;

/// <summary>Thin wrapper over Process for capturing output from external commands.</summary>
public static class ProcessRunner
{
    public static ProcessResult Run(string fileName, IEnumerable<string> args, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;
        if (environment is not null)
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

        using var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            stdout.GetAwaiter().GetResult(),
            stderr.GetAwaiter().GetResult());
    }

    /// <summary>Captures output and throws when the child exits non-zero.</summary>
    public static ProcessResult RunChecked(string fileName, IEnumerable<string> args, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = Run(fileName, args, workingDirectory, environment);
        if (result.ExitCode != 0)
        {
            var rendered = string.Join(" ", new[] { fileName }.Concat(args));
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException($"Command failed ({result.ExitCode}): {rendered}\n{detail}".TrimEnd());
        }
        return result;
    }
}
