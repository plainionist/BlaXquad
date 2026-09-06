namespace squad.Workspaces;

/// <summary>Defines the canonical project and runtime paths derived for a headquarters launch.</summary>
public record ProjectLayout(
    string WorkingDir,
    string ScriptDir,
    string PackDir,
    string WorktreesDir,
    string ConfigFile,
    string RolesDir,
    string ConstitutionFile,
    string StateDir,
    string HandoffLog)
{
    /// <summary>Resolves workspace paths from the requested directory and tool paths from the installed application.</summary>
    public static ProjectLayout Create(string workingDirArg)
    {
        var workingDir = Path.GetFullPath(workingDirArg);
        var scriptDir = AppContext.BaseDirectory.TrimEnd('/', '\\');
        var packDir = Path.Combine(workingDir, "blaxquad");
        var stateDir = Path.Combine(workingDir, ".blaxquad");

        return new ProjectLayout(
            workingDir,
            scriptDir,
            packDir,
            Path.Combine(workingDir, ".worktrees"),
            Path.Combine(packDir, "squad.json"),
            Path.Combine(packDir, "roles"),
            Path.Combine(packDir, "constitution.prompt"),
            stateDir,
            Path.Combine(stateDir, "handoff-delivery.log"));
    }
}


