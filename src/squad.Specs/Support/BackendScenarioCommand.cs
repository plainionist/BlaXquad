using squad.Specs.Support.Processes;

namespace squad.Specs.Support;

/// <summary>
/// Semantic handle for one real CLI command process a <see cref="BackendScenario"/> started without waiting for
/// it to finish - for example "squad-hq wait-for-agent" while its target role is deliberately kept busy. Lets step
/// definitions observe whether the command is still running, or await its bounded completion and captured output,
/// without ever seeing the underlying process.
/// </summary>
public sealed class BackendScenarioCommand
{
    private readonly System.Diagnostics.Process myProcess;
    private readonly Task<string> myStdOut;
    private readonly Task<string> myStdErr;

    internal BackendScenarioCommand(System.Diagnostics.Process process)
    {
        myProcess = process;
        myStdOut = process.StandardOutput.ReadToEndAsync();
        myStdErr = process.StandardError.ReadToEndAsync();
    }

    /// <summary>Whether the command process has not (yet) exited.</summary>
    public bool IsRunning => !myProcess.HasExited;

    /// <summary>Waits up to the given timeout for the command to exit and returns its captured exit code and
    /// output, never exposing the process itself.</summary>
    public async Task<CommandResult> WaitForCompletionAsync(TimeSpan? timeout = null)
    {
        await myProcess.WaitForExitAsync().WaitAsync(timeout ?? TimeSpan.FromSeconds(15));
        return new CommandResult(
            myProcess.StartInfo.FileName,
            [.. myProcess.StartInfo.ArgumentList],
            myProcess.StartInfo.WorkingDirectory,
            myProcess.ExitCode,
            await myStdOut,
            await myStdErr);
    }
}
