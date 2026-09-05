namespace squadHQ.Commands;

static class Shutdown
{
    public static int Run(string[] args)
    {
        if (args.ElementAtOrDefault(0) is "-h" or "--help")
        {
            Console.Error.WriteLine("Usage: squad-hq shutdown [project-root]");
            Console.Error.WriteLine("Stops the squad for the given project (default: current directory).");
            return 1;
        }

        var projectRoot = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Directory.GetCurrentDirectory());
        try
        {
            if (HostControlClient.ShutdownAsync(projectRoot, TimeSpan.FromSeconds(15)).GetAwaiter().GetResult())
                return 0;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
        {
            Console.Error.WriteLine($"Could not contact squad host: {exception.Message}");
            return 1;
        }
        return 0;
    }
}



