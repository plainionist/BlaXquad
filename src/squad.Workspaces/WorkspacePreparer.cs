using squad.Process;
using squad.Configuration;

namespace squad.Workspaces;

/// <summary>
/// Materializes and validates the repository, role worktrees, shared paths, and handoff directories required by a
/// squad launch.
/// </summary>
internal sealed class WorkspacePreparer
{
    public async Task InitializeGitRepoAsync(Ctx ctx, CancellationToken cancellationToken)
    {
        if (Directory.Exists(Path.Combine(ctx.WorkingDir, ".git")) || File.Exists(Path.Combine(ctx.WorkingDir, ".git")))
        {
            return;
        }

        await RunAsync("git", ["init", ctx.WorkingDir], cancellationToken);
        await RunAsync("git", ["-C", ctx.WorkingDir, "branch", "-M", "master"], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialGitignore(ctx);
        await RunAsync("git", ["-C", ctx.WorkingDir, "add", "."], cancellationToken);
        await RunAsync("git", ["-C", ctx.WorkingDir, "commit", "-m", "Initial squad repository"], cancellationToken);
    }

    public async Task EnsureRuntimeGitExcludesAsync(Ctx ctx, CancellationToken cancellationToken)
    {
        var gitPath = (await ProcessRunner.RunCheckedAsync(
            "git", ["-C", ctx.WorkingDir, "rev-parse", "--git-path", "info/exclude"], workingDirectory: ctx.WorkingDir, cancellationToken: cancellationToken)).StdOut.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        var excludeFile = ResolveGitPath(ctx, gitPath);
        Directory.CreateDirectory(Path.GetDirectoryName(excludeFile)!);
        EnsureInFile(excludeFile, ".blaxquad/");
        EnsureInFile(excludeFile, ".worktrees/");
    }

    // "git rev-parse --git-path" prints a path relative to the invoking process's own current directory (not the
    // requested "-C" workspace) whenever the two differ, e.g. launching a workspace while sitting in an unrelated
    // shell directory. Anchor the child git process at the workspace and re-resolve any relative result against it
    // explicitly so exclude-file resolution never depends on this process's own working directory.
    private static string ResolveGitPath(Ctx ctx, string gitPath) =>
        Path.IsPathRooted(gitPath) ? gitPath : Path.GetFullPath(gitPath, ctx.WorkingDir);

    public void Parse(Ctx ctx)
    {
        if (!File.Exists(ctx.ConfigFile))
        {
            throw new WorkspacePreparationException($"Config not found at {ctx.ConfigFile}");
        }
        if (!File.Exists(ctx.ConstitutionFile))
        {
            throw new WorkspacePreparationException($"Constitution prompt not found at {ctx.ConstitutionFile}");
        }

        SquadConfiguration configuration;
        try
        {
            configuration = SquadConfigurationLoader.Load(ctx.ConfigFile, ctx.RolesDir);
        }
        catch (SquadConfigurationException exception)
        {
            throw new WorkspacePreparationException(exception.Message, exception);
        }

        ctx.Members = configuration.Members;
        ctx.Leader = configuration.Leader;
        ctx.SharedWorktreePaths = configuration.SharedWorktreePaths;
        ctx.GitHistoryCommand = configuration.GitHistoryCommand;
    }

    public void PrepareWorkspace(Ctx ctx)
    {
        foreach (var dir in new[] { ctx.StateDir, ctx.WorktreesDir })
        {
            Directory.CreateDirectory(dir);
        }
        CheckHelperScripts(ctx);
    }

    public async Task PrepareWorktreesAsync(Ctx ctx, CancellationToken cancellationToken)
    {
        foreach (var row in ctx.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.WorktreeTarget.IsProjectRoot)
            {
                continue;
            }
            var worktreePath = row.WorktreeTarget.ResolvePath(ctx.WorkingDir, ctx.WorktreesDir);
            var gitPath = Path.Combine(worktreePath, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                continue;
            }
            await RunAsync("git", ["-C", ctx.WorkingDir, "worktree", "add", "--force", "-B", $"squad-{row.WorktreeTarget.Name}", worktreePath, "HEAD"], cancellationToken);
        }
    }

    /// <summary>
    /// Creates missing worktrees and shared links. Unless <paramref name="continueLaunch"/> is set, configured
    /// worktrees are reset to the main checkout's HEAD. Every launch - continued or not - discards each configured
    /// worktree's complete handoff-state directory: handoffs are file-backed state for the current Headquarters
    /// run, not restart-safe state, so "--continue" preserves only worktree (Git) content.
    /// </summary>
    public async Task PrepareConfiguredWorktreesForLaunchAsync(Ctx ctx, bool continueLaunch, CancellationToken cancellationToken)
    {
        await PrepareWorktreesAsync(ctx, cancellationToken);
        if (!continueLaunch)
        {
            var head = (await ProcessRunner.RunCheckedAsync("git", ["-C", ctx.WorkingDir, "rev-parse", "HEAD"], cancellationToken: cancellationToken)).StdOut.Trim();
            foreach (var row in ctx.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.WorktreeTarget.IsProjectRoot)
                {
                    continue;
                }
                var worktreePath = row.WorktreeTarget.ResolvePath(ctx.WorkingDir, ctx.WorktreesDir);
                await RunAsync("git", ["-C", worktreePath, "checkout", "-B", $"squad-{row.WorktreeTarget.Name}", head, "--force"], cancellationToken);
                await RunAsync("git", ["-C", worktreePath, "reset", "--hard", head], cancellationToken);
            }
        }

        ClearConfiguredHandoffs(ctx, cancellationToken);

        PrepareSharedWorktreePaths(ctx, cancellationToken);
    }

    private void PrepareSharedWorktreePaths(Ctx ctx, CancellationToken cancellationToken)
    {
        foreach (var sharedPath in ctx.SharedWorktreePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(ctx.WorkingDir, sharedPath);
            Directory.CreateDirectory(source);
            foreach (var row in ctx.Members)
            {
                if (row.WorktreeTarget.IsProjectRoot)
                {
                    continue;
                }

                var target = Path.Combine(row.WorktreeTarget.ResolvePath(ctx.WorkingDir, ctx.WorktreesDir), sharedPath);
                ReplaceWithSharedDirectoryLink(source, target);
            }
        }
    }

    private void ReplaceWithSharedDirectoryLink(string source, string target)
    {
        if (Path.Exists(target))
        {
            if (IsDirectoryLink(target))
            {
                Directory.Delete(target);
            }
            else if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any())
            {
                Directory.Delete(target);
            }
            else
            {
                throw new WorkspacePreparationException(
                    $"Cannot replace non-empty shared worktree path {target}; move its contents to {source} before launching");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (OperatingSystem.IsWindows())
        {
            Run("cmd", "/c", "mklink", "/J", target, source);
        }
        else
        {
            Directory.CreateSymbolicLink(target, source);
        }
    }

    private static bool IsDirectoryLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public void PrepareHandoffDirs(Ctx ctx)
    {
        string[] subdirs = ["outbox", "sent", "failed", "inbox/new", "inbox/in_process", "inbox/completed"];
        foreach (var row in ctx.Members)
        {
            var worktreePath = row.WorktreeTarget.ResolvePath(ctx.WorkingDir, ctx.WorktreesDir);
            foreach (var dir in subdirs)
            {
                Directory.CreateDirectory(Path.Combine(worktreePath, ".blaxquad", "handoffs", dir));
            }
        }
    }

    /// <summary>Discards each distinct configured worktree's complete ".blaxquad/handoffs" state - every queued,
    /// in-process, completed, sent, and failed handoff, including nested batch directories - before the canonical
    /// queue directories are recreated by <see cref="PrepareHandoffDirs"/>. Handoffs are file-backed state for the
    /// current Headquarters run only, so this runs unconditionally on every launch, continued or not.</summary>
    private static void ClearConfiguredHandoffs(Ctx ctx, CancellationToken cancellationToken)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var worktreePath in ctx.Members.Select(row => row.WorktreeTarget.ResolvePath(ctx.WorkingDir, ctx.WorktreesDir)).Distinct(pathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handoffDirectory = Path.Combine(worktreePath, ".blaxquad", "handoffs");
            if (Directory.Exists(handoffDirectory))
            {
                Directory.Delete(handoffDirectory, recursive: true);
            }
        }
    }

    private void CheckHelperScripts(Ctx ctx)
    {
        foreach (var helper in new[] { "squad" })
        {
            var path = SiblingTool.Resolve(ctx.ScriptDir, helper);
            if (!IsExecutable(path))
            {
                throw new WorkspacePreparationException($"Required helper script not found or not executable: {path}");
            }
        }
    }

    private void EnsureInitialGitignore(Ctx ctx)
    {
        var gitignore = Path.Combine(ctx.WorkingDir, ".gitignore");
        if (!File.Exists(gitignore))
        {
            File.WriteAllText(gitignore, ".blaxquad/\n.worktrees/\n");
        }
        else
        {
            EnsureInFile(gitignore, ".blaxquad/");
            EnsureInFile(gitignore, ".worktrees/");
        }
    }

    private static void EnsureInFile(string file, string pattern)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file))
        {
            File.WriteAllText(file, "");
        }
        var lines = new HashSet<string>(File.ReadAllLines(file));
        if (!lines.Contains(pattern))
        {
            File.AppendAllText(file, pattern + "\n");
        }
    }

    private static void WriteAtomic(string target, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + $".tmp.{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void Run(string file, params string[] args) =>
        ProcessRunner.RunChecked(file, args);

    private static Task<ProcessResult> RunAsync(string file, IEnumerable<string> args, CancellationToken cancellationToken) =>
        ProcessRunner.RunCheckedAsync(file, args, cancellationToken: cancellationToken);

    private bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return true;
        }
        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }
}
