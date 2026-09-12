using squad.AgentProvider.Abstractions;
using squad.Configuration;
using squad.Domain;
using squad.Process;

namespace squad.Workspaces;

/// <summary>
/// Runs the concrete, cancellable launch-preparation pipeline in its two distinct lifetimes: the one-time process
/// preparation that verifies git, initializes the repository and its runtime excludes, prepares the workspace,
/// configured worktrees and handoff directories; and the per-generation preparation that reloads current
/// configuration and role prompts and builds the backend and member context a squad generation is created from.
/// A replacement generation runs only the second part, so it never resets a worktree or clears a durable handoff
/// queue. The mutable <see cref="Ctx"/> used while discovering the repository is owned exclusively by this type and
/// never escapes it; callers receive only the immutable <see cref="PreparedLaunch"/> result.
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

    /// <summary>
    /// Prepares everything whose lifetime is the process, not a squad generation: the git repository and its
    /// runtime excludes, the workspace layout, the configured worktrees with this launch's reset semantics, the
    /// shared worktree paths, and the durable handoff directories. It runs exactly once, before the first
    /// generation is created, and is never repeated for a replacement.
    /// </summary>
    public async Task PrepareProcessAsync(CancellationToken cancellationToken)
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
    }

    /// <summary>
    /// Reloads the current configuration and role prompts and builds the backend, member, leader, and handoff
    /// context one squad generation is created from. It touches no worktree and no durable handoff queue, so it is
    /// the single call a replacement generation needs.
    /// </summary>
    public Task<PreparedLaunch> PrepareGenerationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        myWorkspacePreparer.Parse(myContext);
        cancellationToken.ThrowIfCancellationRequested();

        var members = myContext.Members.Select(member => new SquadMemberDefinition(
            member.Name,
            member.DisplayName,
            member.Role,
            ResolveWorktreePath(myContext, member),
            member.ReceiveMode,
            member.Agent)).ToArray();
        var definition = new SquadDefinition(members, myContext.Leader);
        return Task.FromResult(new PreparedLaunch(
            BuildBackendContext(myContext),
            definition,
            myContext.HandoffLog,
            myContext.GitHistoryCommand));
    }

    private static string ResolveWorktreePath(Ctx context, SquadMemberConfiguration member) =>
        member.WorktreeTarget.ResolvePath(context.WorkingDir, context.WorktreesDir);

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
            context.Members.Select(member => new AgentRoleContext(
                member.Name,
                member.DisplayName,
                ResolveWorktreePath(context, member),
                InitialInstruction(member.Role.Value),
                member.Agent.Permissions,
                member.Agent.Model,
                member.Agent.Effort)).ToArray(),
            environment);
    }

    private static string InitialInstruction(string role) =>
        "Read blaxquad/constitution.prompt, then read every file it refers to recursively, and obey all of those instructions.\n" +
        $"Read blaxquad/roles/{role}.prompt, then read every file it refers to recursively, and follow all of those instructions.\n";
}
