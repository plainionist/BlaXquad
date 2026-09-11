using squad.AgentProvider.Abstractions;
using squad.Configuration;
using squad.Process;

namespace squad.Workspaces;

/// <summary>
/// Runs the one concrete, cancellable launch-preparation pipeline: verifying git, initializing the repository and
/// its runtime excludes, parsing configuration, preparing the workspace and configured worktrees, setting up
/// shared paths, and creating handoff directories. The mutable <see cref="Ctx"/> used while discovering the
/// repository is owned exclusively by this type and never escapes it; callers receive only the immutable
/// <see cref="PreparedLaunch"/> result.
/// </summary>
public sealed class LaunchPreparer
{
    private readonly Ctx myContext;
    private readonly WorkspacePreparer myWorkspacePreparer = new();

    public LaunchPreparer(ProjectLayout layout, bool continueLaunch)
    {
        myContext = new Ctx
        {
            WorkingDir = layout.WorkingDir,
            ScriptDir = layout.ScriptDir,
            PackDir = layout.PackDir,
            WorktreesDir = layout.WorktreesDir,
            ConfigFile = layout.ConfigFile,
            RolesDir = layout.RolesDir,
            ConstitutionFile = layout.ConstitutionFile,
            StateDir = layout.StateDir,
            HandoffLog = layout.HandoffLog,
            ContinueLaunch = continueLaunch,
            Members = [],
        };
    }

    public async Task<PreparedLaunch> PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ExecutableLocator.Exists("git"))
        {
            throw new WorkspacePreparationException("'git' is required but not installed.");
        }

        await myWorkspacePreparer.InitializeGitRepoAsync(myContext, cancellationToken);
        await myWorkspacePreparer.EnsureRuntimeGitExcludesAsync(myContext, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        myWorkspacePreparer.Parse(myContext);
        cancellationToken.ThrowIfCancellationRequested();

        myWorkspacePreparer.PrepareWorkspace(myContext);
        cancellationToken.ThrowIfCancellationRequested();

        await myWorkspacePreparer.PrepareConfiguredWorktreesForLaunchAsync(myContext, myContext.ContinueLaunch, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        myWorkspacePreparer.PrepareHandoffDirs(myContext);

        return new PreparedLaunch(
            BuildBackendContext(myContext),
            myContext.Members.Select(member => member.Member).ToArray(),
            myContext.Leader,
            myContext.Members.Select(member => new RoleRow(member.Member, member.WorktreeName, member.WorktreePath, member.DisplayName, member.ReceiveMode)).ToArray(),
            myContext.HandoffLog);
    }

    private static AgentBackendContext BuildBackendContext(Ctx context)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = new Dictionary<string, string>(comparer);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is not null)
            {
                environment[key] = entry.Value.ToString()!;
            }
        }

        var existingPath = environment.TryGetValue("PATH", out var pathValue) ? pathValue : string.Empty;
        if (string.IsNullOrEmpty(existingPath))
        {
            environment["PATH"] = context.ScriptDir;
        }
        else
        {
            var parts = existingPath.Split(Path.PathSeparator);
            if (!parts.Contains(context.ScriptDir, comparer))
            {
                environment["PATH"] = string.Join(Path.PathSeparator, context.ScriptDir, existingPath);
            }
        }

        return new AgentBackendContext(
            context.WorkingDir,
            context.ScriptDir,
            context.Members.Select(member => new AgentMemberContext(
                member.Member,
                member.Role,
                member.DisplayName,
                member.WorktreePath,
                InitialInstruction(member.Role),
                member.Permissions,
                member.Model,
                member.Effort)).ToArray(),
            environment);
    }

    private static string InitialInstruction(string role) =>
        "Read blaxquad/constitution.prompt, then read every file it refers to recursively, and obey all of those instructions.\n" +
        $"Read blaxquad/roles/{role}.prompt, then read every file it refers to recursively, and follow all of those instructions.\n";
}
