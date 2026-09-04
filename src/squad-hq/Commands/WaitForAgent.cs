using System.Text.Json;

namespace squad_hq.Commands;

internal static class WaitForAgent
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    public static int Run(string[] args)
    {
        if (!TryParse(args, out var role, out var timeout, out var projectRoot))
            return 1;
        try
        {
            HostControlClient.WaitForAgentAsync(projectRoot, role, timeout).GetAwaiter().GetResult();
            Console.WriteLine($"Agent '{role}' is ready.");
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or JsonException or TimeoutException
                or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool TryParse(
        string[] args,
        out string role,
        out TimeSpan timeout,
        out string projectRoot)
    {
        role = "";
        timeout = DefaultTimeout;
        projectRoot = "";
        var projectRootSpecified = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is "-h" or "--help")
            {
                WriteUsage();
                return false;
            }
            if (argument is "--role" or "-role")
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    return UsageError("The role option requires a value.");
                role = args[index];
                continue;
            }
            if (argument == "--timeout")
            {
                if (++index >= args.Length
                    || !double.TryParse(
                        args[index],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var seconds)
                    || !double.IsFinite(seconds)
                    || seconds <= 0
                    || seconds > TimeSpan.MaxValue.TotalSeconds)
                    return UsageError("The timeout must be a positive number of seconds.");
                timeout = TimeSpan.FromSeconds(seconds);
                continue;
            }
            if (argument.StartsWith('-'))
                return UsageError($"Unknown option: {argument}");
            if (role.Length == 0)
            {
                role = argument;
                continue;
            }
            if (!projectRootSpecified)
            {
                projectRoot = Path.GetFullPath(argument);
                projectRootSpecified = true;
                continue;
            }
            return UsageError($"Unexpected argument: {argument}");
        }

        if (role.Length == 0)
            return UsageError("A role is required.");
        projectRoot = projectRootSpecified
            ? Path.GetFullPath(projectRoot)
            : HostProjectRoot.ResolveViaGit();
        return true;
    }

    private static bool UsageError(string message)
    {
        Console.Error.WriteLine(message);
        WriteUsage();
        return false;
    }

    private static void WriteUsage() =>
        Console.Error.WriteLine(
            "Usage: squad-hq wait-for-agent <role> [--timeout <positive-seconds>] [project-root]\n"
            + "Blocks until the role can receive a prompt. The project is inferred from the current Git checkout when omitted.");
}



