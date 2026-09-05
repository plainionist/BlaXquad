using global::squad.Process;
using global::squad.Photino;
using System.Text.RegularExpressions;
using squad.Configuration;

namespace squadHQ.Commands;

public sealed class WorkspacePreparer
{
    private readonly Action<string> myFail;
    public WorkspacePreparer(Action<string> fail)
    {
        myFail = fail;
    }

    public void InitializeGitRepo(Ctx ctx)
    {
        if (Directory.Exists(Path.Combine(ctx.WorkingDir, ".git")) || File.Exists(Path.Combine(ctx.WorkingDir, ".git")))
            return;

        Run("git", "init", ctx.WorkingDir);
        Run("git", "-C", ctx.WorkingDir, "branch", "-M", "master");
        EnsureInitialGitignore(ctx);
        Run("git", "-C", ctx.WorkingDir, "add", ".");
        Run("git", "-C", ctx.WorkingDir, "commit", "-m", "Initial squad repository");
    }

    public async Task InitializeGitRepoAsync(Ctx ctx, CancellationToken cancellationToken)
    {
        if (Directory.Exists(Path.Combine(ctx.WorkingDir, ".git")) || File.Exists(Path.Combine(ctx.WorkingDir, ".git")))
            return;

        await RunAsync("git", ["init", ctx.WorkingDir], cancellationToken);
        await RunAsync("git", ["-C", ctx.WorkingDir, "branch", "-M", "master"], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialGitignore(ctx);
        await RunAsync("git", ["-C", ctx.WorkingDir, "add", "."], cancellationToken);
        await RunAsync("git", ["-C", ctx.WorkingDir, "commit", "-m", "Initial squad repository"], cancellationToken);
    }

    public void EnsureRuntimeGitExcludes(Ctx ctx)
    {
        var excludeFile = ProcessRunner.RunChecked("git", ["-C", ctx.WorkingDir, "rev-parse", "--git-path", "info/exclude"]).StdOut.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(excludeFile)!);
        EnsureInFile(excludeFile, ".blaxquad/");
        EnsureInFile(excludeFile, ".worktrees/");
    }

    public async Task EnsureRuntimeGitExcludesAsync(Ctx ctx, CancellationToken cancellationToken)
    {
        var excludeFile = (await ProcessControl.RunCheckedAsync("git", ["-C", ctx.WorkingDir, "rev-parse", "--git-path", "info/exclude"], cancellationToken: cancellationToken)).StdOut.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(excludeFile)!);
        EnsureInFile(excludeFile, ".blaxquad/");
        EnsureInFile(excludeFile, ".worktrees/");
    }

    public void WriteAgentInstructionFile(string role, string promptFile) =>
        File.WriteAllText(promptFile,
            "Read blaxquad/constitution.prompt, then read every file it refers to recursively, and obey all of those instructions.\n" +
            $"Read blaxquad/roles/{role}.prompt, then read every file it refers to recursively, and follow all of those instructions.\n");

    public void Parse(Ctx ctx)
    {
        if (!File.Exists(ctx.ConfigFile))
            myFail($"{myRed}Error:{myReset} Config not found at {ctx.ConfigFile}");
        if (!File.Exists(ctx.ConstitutionFile))
            myFail($"{myRed}Error:{myReset} Constitution prompt not found at {ctx.ConstitutionFile}");

        SquadConfiguration configuration;
        try
        {
            configuration = SquadConfigurationLoader.Load(ctx.ConfigFile, ctx.RolesDir);
        }
        catch (SquadConfigurationException exception)
        {
            myFail($"{myRed}Error:{myReset} {exception.Message}");
            return;
        }

        ctx.Roles = configuration.Roles.Select(role =>
        {
            var worktreePath = role.Worktree == "master" ? ctx.WorkingDir : Path.Combine(ctx.WorktreesDir, role.Worktree);
            return new RoleConfigRow(
                role.Name,
                DisplayNameForRole(role.Name),
                role.Worktree,
                worktreePath,
                role.ReceiveMode,
                role.Agent.Permissions,
                role.Agent.Model,
                role.Agent.Effort);
        }).ToList();
        ctx.SharedWorktreePaths = configuration.SharedWorktreePaths;
    }

    public void PrepareWorkspace(Ctx ctx)
    {
        foreach (var dir in new[] { ctx.StateDir, ctx.WorktreesDir })
            Directory.CreateDirectory(dir);
        CheckHelperScripts(ctx);
    }

    public void PrepareWorktrees(Ctx ctx)
    {
        foreach (var row in ctx.Roles)
        {
            if (row.WorktreeName is "none" or "master")
                continue;
            var gitPath = Path.Combine(row.WorktreePath, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                continue;
            Run("git", "-C", ctx.WorkingDir, "worktree", "add", "--force", "-B", $"squad-{row.WorktreeName}", row.WorktreePath, "HEAD");
        }
    }

    public async Task PrepareWorktreesAsync(Ctx ctx, CancellationToken cancellationToken)
    {
        foreach (var row in ctx.Roles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.WorktreeName is "none" or "master")
                continue;
            var gitPath = Path.Combine(row.WorktreePath, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                continue;
            await RunAsync("git", ["-C", ctx.WorkingDir, "worktree", "add", "--force", "-B", $"squad-{row.WorktreeName}", row.WorktreePath, "HEAD"], cancellationToken);
        }
    }

    public async Task PrepareConfiguredWorktreesForLaunchAsync(Ctx ctx, bool continueLaunch, CancellationToken cancellationToken)
    {
        await PrepareWorktreesAsync(ctx, cancellationToken);
        if (!continueLaunch)
        {
            var head = (await ProcessControl.RunCheckedAsync("git", ["-C", ctx.WorkingDir, "rev-parse", "HEAD"], cancellationToken: cancellationToken)).StdOut.Trim();
            foreach (var row in ctx.Roles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.WorktreeName is "none" or "master")
                    continue;
                await RunAsync("git", ["-C", row.WorktreePath, "checkout", "-B", $"squad-{row.WorktreeName}", head, "--force"], cancellationToken);
                await RunAsync("git", ["-C", row.WorktreePath, "reset", "--hard", head], cancellationToken);
            }

            ClearConfiguredHandoffs(ctx, cancellationToken);
        }

        PrepareSharedWorktreePaths(ctx, cancellationToken);
    }

    private void PrepareSharedWorktreePaths(Ctx ctx, CancellationToken cancellationToken)
    {
        foreach (var sharedPath in ctx.SharedWorktreePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(ctx.WorkingDir, sharedPath);
            Directory.CreateDirectory(source);
            foreach (var row in ctx.Roles)
            {
                if (row.WorktreeName is "none" or "master")
                    continue;

                var target = Path.Combine(row.WorktreePath, sharedPath);
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
                myFail($"{myRed}Error:{myReset} Cannot replace non-empty shared worktree path {target}; move its contents to {source} before launching");
                return;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (OperatingSystem.IsWindows())
            Run("cmd", "/c", "mklink", "/J", target, source);
        else
            Directory.CreateSymbolicLink(target, source);
    }

    private static bool IsDirectoryLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public void PrepareHandoffDirs(Ctx ctx)
    {
        string[] subdirs = ["outbox/tmp", "sent", "failed", "inbox/new", "inbox/in_process", "inbox/completed"];
        foreach (var row in ctx.Roles)
            foreach (var dir in subdirs)
                Directory.CreateDirectory(Path.Combine(row.WorktreePath, ".blaxquad", "handoffs", dir));
    }

    private static void ClearConfiguredHandoffs(Ctx ctx, CancellationToken cancellationToken)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var worktreePath in ctx.Roles.Select(row => row.WorktreePath).Distinct(pathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handoffDirectory = Path.Combine(worktreePath, ".blaxquad", "handoffs");
            foreach (var directory in new[] { "inbox/new", "inbox/in_process", "inbox/completed", "outbox", "sent", "failed" })
            {
                var path = Path.Combine(handoffDirectory, directory);
                if (!Directory.Exists(path))
                    continue;
                foreach (var handoff in Directory.EnumerateFiles(path, "*.handoff"))
                    File.Delete(handoff);
            }

            var inbox = Path.Combine(handoffDirectory, "inbox");
            if (!Directory.Exists(inbox))
                continue;
            foreach (var bucket in Directory.EnumerateDirectories(inbox))
                foreach (var batch in Directory.EnumerateDirectories(bucket, "batch_*"))
                    Directory.Delete(batch, recursive: true);
        }
    }

    private void CheckHelperScripts(Ctx ctx)
    {
        foreach (var helper in new[] { "squad" })
        {
            var path = SiblingTool.Resolve(ctx.ScriptDir, helper);
            if (!IsExecutable(path))
                myFail($"{myRed}Error:{myReset} Required helper script not found or not executable: {path}");
        }
    }

    private void EnsureInitialGitignore(Ctx ctx)
    {
        var gitignore = Path.Combine(ctx.WorkingDir, ".gitignore");
        if (!File.Exists(gitignore))
            File.WriteAllText(gitignore, ".blaxquad/\n.worktrees/\n");
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
            File.WriteAllText(file, "");
        var lines = new HashSet<string>(File.ReadAllLines(file));
        if (!lines.Contains(pattern))
            File.AppendAllText(file, pattern + "\n");
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
                File.Delete(temporary);
        }
    }

    private void Run(string file, params string[] args) =>
        ProcessRunner.RunChecked(file, args);

    private static Task<ProcessResult> RunAsync(string file, IEnumerable<string> args, CancellationToken cancellationToken) =>
        ProcessControl.RunCheckedAsync(file, args, cancellationToken: cancellationToken);

    private bool IsExecutable(string path)
    {
        if (!File.Exists(path))
            return false;
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return true;
        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }

    private static string DisplayNameForRole(string role) =>
        string.Join(" ", Regex.Split(Regex.Replace(role, "[-_]", " "), @"\s+")
            .Where(s => s.Length > 0)
            .Select(value => value.Length == 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant()));

    private const string myRed = "\u001b[0;31m";
    private const string myReset = "\u001b[0m";
}



