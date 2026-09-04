namespace squad.Agent.Tooling;

/// <summary>Resolves a sibling tool binary in the published bin/ folder.</summary>
public static class SiblingTool
{
    public static string Resolve(string directory, string toolName)
    {
        if (OperatingSystem.IsWindows())
        {
            var exe = System.IO.Path.Combine(directory, toolName + ".exe");
            if (File.Exists(exe))
                return exe;

            var fixtureExe = System.IO.Path.Combine(directory, "squad-tools", toolName + ".exe");
            if (File.Exists(fixtureExe))
                return fixtureExe;
        }

        var candidate = System.IO.Path.Combine(directory, toolName);
        if (File.Exists(candidate))
            return candidate;

        var fixtureCandidate = System.IO.Path.Combine(directory, "squad-tools", toolName);
        if (File.Exists(fixtureCandidate))
            return fixtureCandidate;

        return candidate;
    }
}



