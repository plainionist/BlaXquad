using System.Diagnostics;
using global::squad.Process;

namespace squad.Photino;

public static class ProcessControl
{
    public static bool CommandExists(string command)
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

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in args)
            psi.ArgumentList.Add(argument);
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;
        if (environment is not null)
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

        using var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    public static async Task<ProcessResult> RunCheckedAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(fileName, args, workingDirectory, environment, cancellationToken);
        if (result.ExitCode != 0)
        {
            var rendered = string.Join(" ", new[] { fileName }.Concat(args));
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException($"Command failed ({result.ExitCode}): {rendered}\n{detail}".TrimEnd());
        }
        return result;
    }

    public static System.Diagnostics.Process StartDetached(IReadOnlyList<string> command, string? stdOutErrFile = null)
    {
        if (command.Count == 0)
            throw new ArgumentException("Command must not be empty.", nameof(command));

        var psi = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = stdOutErrFile is not null,
            RedirectStandardError = stdOutErrFile is not null,
        };
        foreach (var argument in command.Skip(1))
            psi.ArgumentList.Add(argument);

        var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{command[0]}'.");
        if (stdOutErrFile is not null)
            _ = CaptureOutputAsync(process, stdOutErrFile);
        return process;
    }

    public static async Task TerminateAsync(System.Diagnostics.Process process)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static async Task CaptureOutputAsync(System.Diagnostics.Process process, string outputFile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            await using var writer = new StreamWriter(new FileStream(outputFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
            var synchronizedWriter = TextWriter.Synchronized(writer);
            await Task.WhenAll(
                PumpOutputAsync(process.StandardOutput, synchronizedWriter),
                PumpOutputAsync(process.StandardError, synchronizedWriter));
        }
        catch
        {
            // Detached process output capture must not terminate its owner.
        }
    }

    private static async Task PumpOutputAsync(StreamReader reader, TextWriter writer)
    {
        while (await reader.ReadLineAsync() is { } line)
            await writer.WriteLineAsync(line);
    }
}



