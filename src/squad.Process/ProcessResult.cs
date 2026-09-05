namespace squad.Process;

public readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);
