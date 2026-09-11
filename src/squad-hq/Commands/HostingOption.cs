using squad.Process;

namespace squadHQ.Commands;

/// <summary>Parses the optional "--hosting &lt;assemblyPath&gt;;&lt;typeName&gt;" launch option. Replaces the
/// former built-in "--ui photino|stdio" choice: omitting this option launches the packaged default Photino
/// hosting plug-in, exactly like omitting "--provider" launches the packaged default provider.</summary>
static class HostingOption
{
    private const string Flag = "--hosting";

    /// <summary>Extracts at most one "--hosting" option from <paramref name="args"/>, returning the parsed
    /// descriptor (or null if omitted) plus the remaining arguments with the option removed.</summary>
    public static (HostingDescriptor? Descriptor, string[] Remaining) Extract(string[] args)
    {
        HostingDescriptor? descriptor = null;
        var remaining = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != Flag)
            {
                remaining.Add(args[i]);
                continue;
            }

            if (descriptor is not null)
            {
                throw new CliExitException(1, $"The {Flag} option may only be specified once.");
            }

            if (i + 1 >= args.Length)
            {
                throw new CliExitException(1, $"The {Flag} option requires a value in the form <assemblyPath>;<typeName>.");
            }

            descriptor = Parse(args[++i]);
        }

        return (descriptor, remaining.ToArray());
    }

    private static HostingDescriptor Parse(string value)
    {
        var separatorIndex = value.IndexOf(';');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            throw new CliExitException(1, $"The {Flag} value must be in the form <assemblyPath>;<typeName>, got '{value}'.");
        }

        var assemblyPath = value[..separatorIndex];
        var typeName = value[(separatorIndex + 1)..];

        // Relative assembly paths resolve against the launcher's own current directory, never the target workspace.
        var resolvedAssemblyPath = Path.GetFullPath(assemblyPath, Directory.GetCurrentDirectory());
        return new HostingDescriptor(resolvedAssemblyPath, typeName);
    }
}
