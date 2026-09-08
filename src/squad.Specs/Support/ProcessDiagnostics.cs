namespace squad.Specs.Support;

/// <summary>
/// Formats a one-line snapshot of a child process's command and its current lifecycle state (running with its
/// process id, or exited with its exit code) for inclusion in bounded-wait timeout diagnostics across workspace,
/// CLI, UI, and lifecycle support - without ever exposing the process itself to step definitions.
/// </summary>
internal static class ProcessDiagnostics
{
    public static string Describe(System.Diagnostics.Process process)
    {
        var command = $"{process.StartInfo.FileName} {string.Join(' ', process.StartInfo.ArgumentList)}";
        var state = DescribeState(process);
        return $"{command}\nState: {state}";
    }

    private static string DescribeState(System.Diagnostics.Process process)
    {
        try
        {
            return process.HasExited
                ? $"exited with code {process.ExitCode}"
                : $"running (pid {process.Id})";
        }
        catch (InvalidOperationException)
        {
            // The process object may already be disposed by the time diagnostics are formatted (for example,
            // after a concurrent emergency cleanup); report that plainly instead of throwing while diagnosing.
            return "state unavailable (process object no longer accessible)";
        }
    }
}
