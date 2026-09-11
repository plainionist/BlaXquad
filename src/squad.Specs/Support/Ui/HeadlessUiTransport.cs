using System.Diagnostics;
using System.Linq;
using squad.Specs.Support.Processes;

namespace squad.Specs.Support.Ui;

/// <summary>
/// Owns the raw stdio transport behind one launched, stdio-hosted squad-hq process: writing framed lines to
/// standard input, draining standard output and standard error concurrently into synchronized snapshots so a
/// full stderr pipe can never block a pending stdout read (or vice versa), closing standard input to signal the
/// process the UI is gone, and rendering the combined process/output diagnostics block a bounded wait reports on
/// timeout. Carries no protocol semantics of its own - <see cref="HeadlessUiClient"/> supplies the "Last known UI
/// state" section through <paramref name="describeUiState"/> in <see cref="DescribeDiagnostics"/> because only it
/// can interpret the protocol well enough to summarize it.
/// </summary>
internal sealed class HeadlessUiTransport
{
    private readonly Process myProcess;
    private readonly TextWriter myStandardInput;
    private readonly object myLinesLock = new();
    private readonly List<string> myStdOutLines = [];
    private readonly List<string> myStdErrLines = [];

    public HeadlessUiTransport(Process process, TextWriter standardInput, TextReader standardOutput, TextReader standardError)
    {
        myProcess = process;
        myStandardInput = standardInput;
        Drain(standardOutput, myStdOutLines);
        Drain(standardError, myStdErrLines);
    }

    /// <summary>Writes one already-framed line to standard input and flushes it immediately.</summary>
    public void WriteLine(string line)
    {
        myStandardInput.WriteLine(line);
        myStandardInput.Flush();
    }

    /// <summary>
    /// Closes the process's standard input, the same observable event as a real UI process exiting or its window
    /// closing - squad-hq treats end of standard input as the "the UI is gone" signal regardless of which launcher
    /// created the process.
    /// </summary>
    public void CloseStandardInput() => myStandardInput.Close();

    /// <summary>A synchronized snapshot of every standard-output line captured from the launched process so far.</summary>
    public IReadOnlyList<string> CopyStdOutLines() => CopyLines(myStdOutLines);

    /// <summary>A synchronized snapshot of every standard-error line captured from the launched process so far.</summary>
    public IReadOnlyList<string> CopyStdErrLines() => CopyLines(myStdErrLines);

    /// <summary>
    /// Builds a diagnostics snapshot of the launched process's command/lifecycle state, the caller-supplied "Last
    /// known UI state" summary, and the captured standard output and standard error, in that fixed order.
    /// </summary>
    public string DescribeDiagnostics(IReadOnlyList<string> stdOutSnapshot, Func<string> describeUiState, Func<string>? additionalDiagnostics)
    {
        var core = $"""
            Process:
            {ProcessDiagnostics.Describe(myProcess)}
            Last known UI state:
            {describeUiState()}
            StdOut:
            {FormatCapturedLines(stdOutSnapshot)}
            StdErr:
            {FormatCapturedLines(CopyStdErrLines())}
            """;
        return additionalDiagnostics is null ? core : $"{core}\n{additionalDiagnostics()}";
    }

    private static string FormatCapturedLines(IReadOnlyList<string> lines) => lines.Count switch
    {
        0 => "(none)",
        <= 10 => string.Join('\n', lines),
        _ => $"... ({lines.Count - 10} earlier lines omitted)\n" + string.Join('\n', lines.TakeLast(10)),
    };

    private void Drain(TextReader reader, List<string> destination) =>
        Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (myLinesLock)
                {
                    destination.Add(line);
                }
            }
        });

    private List<string> CopyLines(List<string> lines)
    {
        lock (myLinesLock)
        {
            return [.. lines];
        }
    }
}
