namespace squad.Agent;

/// <summary>Thrown to request the process exit with a specific code, optionally printing a message to stderr first.</summary>
public sealed class CliExitException : Exception
{
    public int ExitCode { get; }

    public CliExitException(int exitCode, string? message = null) : base(message)
    {
        ExitCode = exitCode;
    }
}



