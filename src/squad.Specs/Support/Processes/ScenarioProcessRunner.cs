using System.Diagnostics;
using System.Text.RegularExpressions;

namespace squad.Specs.Support.Processes;

/// <summary>
/// Owns the scenario-level process mechanics behind every command <see cref="Support.ScenarioWorkspace"/> runs or
/// starts: building <see cref="ProcessStartInfo"/>, redirected-stream capture, ANSI-stripped output normalization,
/// tracking every process a scenario launches - including ones started outside this runner, such as through
/// <see cref="CancellableChildProcess"/> - and bounded termination of all of them during scenario teardown.
/// <see cref="Support.ScenarioWorkspace"/> itself still chooses published tools, working directories, and Git
/// identity, and records the latest <see cref="CommandResult"/>.
/// </summary>
internal sealed class ScenarioProcessRunner : IDisposable
{
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);
    private readonly List<System.Diagnostics.Process> myRunningProcesses = [];

    public System.Diagnostics.Process Start(
        string executable,
        IReadOnlyList<string>? arguments,
        IReadOnlyDictionary<string, string?>? environment,
        string workingDirectory,
        bool redirectStandardInput)
    {
        var startInfo = CreateStartInfo(executable, environment, workingDirectory);
        startInfo.RedirectStandardInput = redirectStandardInput;
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        myRunningProcesses.Add(process);
        return process;
    }

    public CommandResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
        string workingDirectory)
    {
        var startInfo = CreateStartInfo(executable, environment, workingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return new CommandResult(
            executable,
            arguments,
            startInfo.WorkingDirectory,
            process.ExitCode,
            Normalize(stdout.GetAwaiter().GetResult()),
            Normalize(stderr.GetAwaiter().GetResult()));
    }

    /// <summary>
    /// Registers a process launched outside <see cref="Start"/> (for example, through
    /// <see cref="CancellableChildProcess"/>) so this scenario's own emergency cleanup on <see cref="Dispose"/>
    /// still terminates it if a specification never reaches its own normal shutdown.
    /// </summary>
    public void TrackProcess(System.Diagnostics.Process process) => myRunningProcesses.Add(process);

    /// <summary>
    /// Stops every process this scenario started or tracked. Each stop is bounded and isolated: a process that
    /// will not stop is logged and skipped rather than left to hang or to throw out of <see cref="Dispose"/> - a
    /// cleanup failure here must never replace a scenario's real failure.
    /// </summary>
    public void Dispose()
    {
        foreach (var process in myRunningProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                process.WaitForExit((int)ProcessExitTimeout.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"ScenarioProcessRunner cleanup: failed to stop a child process: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string Normalize(string value) =>
        AnsiEscape.Replace(value.Replace("\r\n", "\n"), "");

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyDictionary<string, string?>? environment,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }
        return startInfo;
    }
}
