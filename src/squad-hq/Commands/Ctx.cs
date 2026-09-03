namespace squad_hq.Commands;

public class Ctx
{
    public required string WorkingDir { get; set; }
    public required string ScriptDir { get; set; }
    public string PackDir { get; set; } = "";
    public string WorktreesDir { get; set; } = "";
    public string ConfigFile { get; set; } = "";
    public string RolesDir { get; set; } = "";
    public string ConstitutionFile { get; set; } = "";
    public string StateDir { get; set; } = "";
    public string HandoffLog { get; set; } = "";
    public bool ContinueLaunch { get; set; }
    public List<RoleConfigRow> Roles { get; set; } = [];
    public IReadOnlyList<string> SharedWorktreePaths { get; set; } = [];
}



