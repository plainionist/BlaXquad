using global::squad.Agent;
using global::squad.Agent.Cli;
using global::squad.Commands;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: squad <command> [args...]");
    Console.Error.WriteLine("Agent commands: handoff, context, ready-for-next, done-with-current");
    return 1;
}

try
{
    return args[0] switch
    {
    "ready-for-next-task" => ReadyForNextTask.Run(args[1..]),
    "ready-for-next-batch" => ReadyForNextBatch.Run(args[1..]),
    "done-with-current-task" => DoneWithCurrentTask.Run(args[1..]),
    "done-with-current-batch" => DoneWithCurrentBatch.Run(args[1..]),
    "ready-for-next" => ReadyForNext.Run(args[1..]),
    "done-with-current" => DoneWithCurrent.Run(args[1..]),
    "handoff" => Handoff.Run(args[1..]),
    "context" => Context.Run(args[1..]),
    _ => Unknown(args[0]),
    };
}
catch (CliExitException exception)
{
    if (!string.IsNullOrEmpty(exception.Message))
        Console.Error.WriteLine(exception.Message);
    return exception.ExitCode;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown squad command '{command}'.");
    return 1;
}



