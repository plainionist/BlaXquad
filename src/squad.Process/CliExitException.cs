namespace squad.Process;

/// <summary>Thrown to request the process exit with a specific code, optionally printing a message to stderr first.</summary>
public sealed class CliExitException : Exception
{
    public int ExitCode { get; }

    public CliExitException(int exitCode, string? message = null) : base(message)
    {
        Contract.Requires(exitCode != 0, "exitCode must not be zero.");
        ExitCode = exitCode;
    }

    public CliExitException(int exitCode, string? message, Exception? innerException) : base(message, innerException)
    {
        Contract.Requires(exitCode != 0, "exitCode must not be zero.");
        ExitCode = exitCode;
    }
}
