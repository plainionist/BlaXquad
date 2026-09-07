using squad.Process;

namespace squadHQ.Commands;

/// <summary>Parses the optional "--ui &lt;photino|stdio&gt;" launch option.</summary>
static class UiOption
{
    private const string Flag = "--ui";
    private const string Photino = "photino";
    private const string Stdio = "stdio";

    /// <summary>Extracts at most one "--ui" option from <paramref name="args"/>, returning the selected mode
    /// (defaulting to Photino when omitted) plus the remaining arguments with the option removed.</summary>
    public static (UiMode Mode, string[] Remaining) Extract(string[] args)
    {
        UiMode? mode = null;
        var remaining = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != Flag)
            {
                remaining.Add(args[i]);
                continue;
            }

            if (mode is not null)
            {
                throw new CliExitException(1, $"The {Flag} option may only be specified once.");
            }

            if (i + 1 >= args.Length)
            {
                throw new CliExitException(1, $"The {Flag} option requires a value in the form <photino|stdio>.");
            }

            mode = Parse(args[++i]);
        }

        return (mode ?? UiMode.Photino, remaining.ToArray());
    }

    private static UiMode Parse(string value) =>
        value switch
        {
            Photino => UiMode.Photino,
            Stdio => UiMode.Stdio,
            _ => throw new CliExitException(1, $"The {Flag} value must be 'photino' or 'stdio', got '{value}'."),
        };
}

internal enum UiMode
{
    Photino,
    Stdio,
}
