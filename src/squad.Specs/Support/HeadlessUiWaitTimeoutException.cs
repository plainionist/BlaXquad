namespace squad.Specs.Support;

/// <summary>
/// Reports a bounded <see cref="HeadlessUiClient"/> semantic wait failure. Captures the launched process's full
/// standard output and standard error plus the most recently observed UI state, so a timeout can be diagnosed
/// without re-running the scenario or inspecting raw protocol traffic by hand.
/// </summary>
public sealed class HeadlessUiWaitTimeoutException : TimeoutException
{
    public HeadlessUiWaitTimeoutException(
        string description,
        IReadOnlyList<string> capturedStdOut,
        IReadOnlyList<string> capturedStdErr,
        string lastKnownUiState)
        : base(BuildMessage(description, capturedStdOut, capturedStdErr, lastKnownUiState))
    {
    }

    private static string BuildMessage(
        string description,
        IReadOnlyList<string> capturedStdOut,
        IReadOnlyList<string> capturedStdErr,
        string lastKnownUiState) =>
        $"""
        Timed out waiting for {description}.
        Last known UI state:
        {lastKnownUiState}
        StdOut:
        {string.Join('\n', capturedStdOut)}
        StdErr:
        {string.Join('\n', capturedStdErr)}
        """;
}
