using global::squad.Process;
using global::squadHQ.Commands;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: squad-hq <command> [args...]");
    Console.Error.WriteLine("Squad management commands: launch, shutdown, wait-for-agent");
    return 1;
}

try
{
    return args[0] switch
    {
        "shutdown" => Shutdown.Run(args[1..]),
        "wait-for-agent" => WaitForAgent.Run(args[1..]),
        "launch" => Launch.Run(args[1..]),
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
    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine("Usage: squad-hq <command> [args...]");
    return 1;
}



