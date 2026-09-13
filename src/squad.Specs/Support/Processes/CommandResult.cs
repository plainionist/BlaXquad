namespace squad.Specs.Support.Processes;

/// <summary>
/// The exact command line, working directory, and captured output of one scenario-owned child
/// process, so a failing command can be diagnosed without re-running it.
/// </summary>
public sealed record CommandResult(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int ExitCode,
    string StdOut,
    string StdErr)
{
    public void EnsureSuccess()
    {

        if (ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"""
            Command failed: {Executable} {string.Join(' ', Arguments)}
            Working directory: {WorkingDirectory}
            Exit code: {ExitCode}
            StdOut:
            {StdOut}
            StdErr:
            {StdErr}
            """);
    }
}
