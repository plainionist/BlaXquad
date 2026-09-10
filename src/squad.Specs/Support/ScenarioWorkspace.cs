using System.Diagnostics;
using System.Text.RegularExpressions;

namespace squad.Specs.Support;

public sealed class ScenarioWorkspace : IDisposable
{
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WorkspaceCleanupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WorkspaceCleanupPollInterval = TimeSpan.FromMilliseconds(100);
    private readonly Dictionary<string, object> myValues = new(StringComparer.Ordinal);
    private readonly List<System.Diagnostics.Process> myRunningProcesses = [];
    private readonly Dictionary<string, string> myRoleWorktrees = new(StringComparer.Ordinal);

    public ScenarioWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "blaxquad-specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string RepositoryRootPath => RepositoryRoot();
    public CommandResult? LastResult { get; private set; }

    public string PathInWorkspace(params string[] parts) =>
        parts.Aggregate(Root, Path.Combine);

    public void Set<T>(string key, T value) where T : notnull => myValues[key] = value;

    public T Get<T>(string key) => (T)myValues[key];

    public void WriteFile(string relativePath, string content) => WriteFileUnder(Root, relativePath, content);

    /// <summary>
    /// Writes a file into a role's worktree recorded by <see cref="ConfigureProject"/>, so specifications can seed
    /// role-owned content (for example an agent's own working-tree change) without resolving or retaining the
    /// worktree path themselves.
    /// </summary>
    public void WriteFileInRoleWorktree(string role, string relativePath, string content) =>
        WriteFileUnder(myRoleWorktrees[role], relativePath, content);

    /// <summary>
    /// The worktree path recorded for a role by <see cref="ConfigureProject"/>, exposed so test-owned support (never
    /// step definitions) can locate role-scoped durable state such as a mailbox.
    /// </summary>
    public string RoleWorktreePath(string role) => myRoleWorktrees[role];

    /// <summary>
    /// Poisons a role's already-created handoff outbox directory by replacing it with a directory link (a Windows
    /// junction, or a symbolic link elsewhere) whose target no longer exists, so the real, filesystem-polling
    /// production handoff poller genuinely faults the next time it scans that role's outbox - a deterministic,
    /// real filesystem fault, never an injected pump failure or any other test hook. The link itself still
    /// resolves as a present directory, but enumerating its contents throws because its target is gone, matching a
    /// real, unrecoverable filesystem fault a production deployment could hit (for example a broken mount or a
    /// directory removed out from under a running process). <see cref="Dispose"/> already knows how to remove a
    /// dangling reparse point like this one during workspace teardown.
    /// </summary>
    public void PoisonRoleHandoffOutbox(string role)
    {
        var outboxDir = Path.Combine(myRoleWorktrees[role], ".blaxquad", "handoffs", "outbox");
        if (Directory.Exists(outboxDir))
        {
            Directory.Delete(outboxDir, recursive: true);
        }

        var brokenTarget = Path.Combine(Path.GetTempPath(), "blaxquad-specs-poisoned", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(brokenTarget);
        if (OperatingSystem.IsWindows())
        {
            AssertSuccessful(Run("cmd", ["/c", "mklink", "/J", outboxDir, brokenTarget]));
        }
        else
        {
            Directory.CreateSymbolicLink(outboxDir, brokenTarget);
        }
        Directory.Delete(brokenTarget);
    }

    /// <summary>
    /// Repairs a role's handoff outbox directory after <see cref="PoisonRoleHandoffOutbox"/>, removing the dangling
    /// directory link and recreating the outbox as a normal, empty directory - standing in for the real-world
    /// remediation (fixing a broken mount, replacing a missing shared directory) an operator would have to perform
    /// before a subsequent process could use that role's outbox again. Production code has no reason to self-heal a
    /// genuinely broken directory link, so a healthy subsequent launch against the same workspace is only possible
    /// once the underlying fault is actually fixed.
    /// </summary>
    public void RepairRoleHandoffOutbox(string role)
    {
        var outboxDir = Path.Combine(myRoleWorktrees[role], ".blaxquad", "handoffs", "outbox");
        if ((File.GetAttributes(outboxDir) & FileAttributes.ReparsePoint) != 0)
        {
            File.SetAttributes(outboxDir, FileAttributes.Normal);
            Directory.Delete(outboxDir);
        }
        Directory.CreateDirectory(outboxDir);
    }

    private static void WriteFileUnder(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, Path.Combine(relativePath.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var normalized = content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        File.WriteAllText(path, normalized);
    }

    public CommandResult RunTool(
        string toolName,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var executable = ResolveTool(toolName, "squad-tools");
        return Run(executable, arguments ?? [], environment, workingDirectory);
    }

    /// <summary>
    /// The exact executable path used by <see cref="RunBackendSpecSquadHq"/>, exposed so
    /// specifications can prove a command ran that precise publication rather than PATH, a
    /// checkout binary, or the production-like squad-tools publication.
    /// </summary>
    public string BackendSpecSquadHqExecutablePath => ResolveTool("squad-hq", "squad-tools-backend-spec");

    /// <summary>
    /// Runs the exact published, provider-free squad-hq used by backend specifications (published
    /// with IncludeCopilotSdkProvider=false into its own test-output directory), never the
    /// production-like squad-tools publication, PATH, or a checkout binary.
    /// </summary>
    public CommandResult RunBackendSpecSquadHq(
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var executable = BackendSpecSquadHqExecutablePath;
        return Run(executable, arguments ?? [], environment, workingDirectory);
    }

    /// <summary>
    /// Creates a uniquely rooted, configured Git project with one linked worktree per role,
    /// records each role's worktree path behind this workspace (see <see cref="RunRoleTool"/>),
    /// and also returns those paths so specifications do not duplicate project bootstrap.
    /// </summary>
    public IReadOnlyDictionary<string, string> ConfigureProject(params string[] roles)
    {
        if (roles.Length == 0)
        {
            throw new ArgumentException("At least one role is required.", nameof(roles));
        }

        InitializeGitRepository();
        WriteFile("blaxquad/constitution.prompt", "Follow the project constitution.\n");

        foreach (var role in roles)
        {
            WriteFile($"blaxquad/roles/{role}.prompt", $"Act as the {role}.\n");
            var worktreePath = PathInWorkspace(".worktrees", role);
            AssertSuccessful(RunGit("worktree", "add", "--quiet", "-b", $"squad-{role}", worktreePath));
            myRoleWorktrees[role] = worktreePath;
        }

        var rolesJson = string.Join(",\n", roles.Select(role =>
            $$"""    { "name": "{{role}}", "worktree": "{{role}}", "agent": {} }"""));
        WriteFile("blaxquad/squad.json", $$"""
            {
              "roles": [
            {{rolesJson}}
              ]
            }
            """ + "\n");

        return myRoleWorktrees;
    }

    /// <summary>
    /// Rewrites a single role already configured by <see cref="ConfigureProject"/> to the given receive mode,
    /// including an empty string, while keeping its existing worktree mapping. Lets scenarios that arrange an
    /// unsupported or missing receive mode do so as a semantic workspace operation instead of serializing
    /// configuration in step definitions.
    /// </summary>
    public void SetRoleReceiveMode(string role, string receiveMode) =>
        WriteFile("blaxquad/squad.json", $$"""
            {
              "roles": [
                { "name": "{{role}}", "worktree": "{{role}}", "receiveMode": "{{receiveMode}}", "agent": {} }
              ]
            }
            """ + "\n");

    /// <summary>
    /// Creates a Git project where every named role maps onto the same repository root (a "master" worktree)
    /// instead of <see cref="ConfigureProject"/>'s one-worktree-per-role layout, for scenarios that arrange an
    /// ambiguous current-worktree identity as a semantic workspace operation.
    /// </summary>
    public void ConfigureProjectWithRolesSharingWorktree(params string[] roles)
    {
        InitializeGitRepository();
        var rolesJson = string.Join(",\n", roles.Select(role =>
            $$"""    { "name": "{{role}}", "worktree": "master", "agent": {} }"""));
        WriteFile("blaxquad/squad.json", $$"""
            {
              "roles": [
            {{rolesJson}}
              ]
            }
            """ + "\n");
    }

    /// <summary>
    /// Runs the exact published tool for a role's worktree recorded by <see cref="ConfigureProject"/>, so step
    /// definitions invoke role-scoped commands without retaining or inspecting worktree paths themselves.
    /// </summary>
    public CommandResult RunRoleTool(
        string role,
        string toolName,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        RunTool(toolName, arguments, environment, myRoleWorktrees[role]);

    public System.Diagnostics.Process StartTool(
        string toolName,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null,
        bool redirectStandardInput = false)
    {
        return StartProcess(ResolveTool(toolName, "squad-tools"), arguments, environment, workingDirectory, redirectStandardInput);
    }

    /// <summary>
    /// Launches the published squad-hq with "--ui stdio" and the given test-owned provider fixture, and returns a
    /// <see cref="HeadlessUiClient"/> already attached to it. Test support - never step definitions - selects the
    /// published tool, builds the provider descriptor, starts and owns the child process, and constructs the
    /// client, matching the architectural split between workspace/CLI support and the semantic UI client.
    /// </summary>
    public HeadlessUiClient StartHeadlessUiClient<TProviderFactory>()
        where TProviderFactory : squad.AgentProvider.Abstractions.IAgentProviderFactory
    {
        var descriptor = $"{typeof(TProviderFactory).Assembly.Location};{typeof(TProviderFactory).FullName}";
        var process = StartTool(
            "squad-hq",
            ["launch", "--provider", descriptor, "--ui", "stdio", Root],
            redirectStandardInput: true);
        return new HeadlessUiClient(process);
    }

    public System.Diagnostics.Process StartProcess(
        string executable,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null,
        bool redirectStandardInput = false)
    {
        var startInfo = CreateStartInfo(executable, environment, workingDirectory);
        startInfo.RedirectStandardInput = redirectStandardInput;
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        myRunningProcesses.Add(process);
        return process;
    }

    /// <summary>
    /// Registers a process launched outside <see cref="StartProcess"/> (for example, through
    /// <see cref="CancellableChildProcess"/>) so this workspace's own emergency cleanup on <see cref="Dispose"/>
    /// still terminates it if a specification never reaches its own normal shutdown.
    /// </summary>
    public void TrackProcess(System.Diagnostics.Process process) => myRunningProcesses.Add(process);

    public void WaitUntil(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            Thread.Sleep(25);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    public CommandResult RunGit(params string[] arguments) =>
        Run("git", arguments, workingDirectory: Root);

    /// <summary>
    /// Runs Git inside a role's worktree recorded by <see cref="ConfigureProject"/>, so a role's own commits are
    /// made on its branch and worktree rather than the shared repository root.
    /// </summary>
    public CommandResult RunRoleGit(string role, params string[] arguments) =>
        Run("git", arguments, workingDirectory: myRoleWorktrees[role]);

    public void InitializeGitRepository()
    {
        AssertSuccessful(RunGit("init", "--quiet"));
        WriteFile("README.md", "# Acceptance fixture\n");
        AssertSuccessful(RunGit("add", "."));
        AssertSuccessful(RunGit("commit", "--quiet", "-m", "Initial fixture"));
    }

    public CommandResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var startInfo = CreateStartInfo(executable, environment, workingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        LastResult = new CommandResult(
            executable,
            arguments,
            startInfo.WorkingDirectory,
            process.ExitCode,
            Normalize(stdout.GetAwaiter().GetResult()),
            Normalize(stderr.GetAwaiter().GetResult()));
        return LastResult;
    }

    /// <summary>
    /// Stops every process this workspace started and removes its temporary directory. Each step is bounded and
    /// isolated: a process that will not stop, or a directory entry that will not delete, is logged and skipped
    /// rather than left to hang or to throw out of <see cref="Dispose"/> - a cleanup failure here must never
    /// replace a scenario's real failure.
    /// </summary>
    public void Dispose()
    {
        foreach (var process in myRunningProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                process.WaitForExit((int)ProcessExitTimeout.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"ScenarioWorkspace cleanup: failed to stop a child process: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        if (!Directory.Exists(Root))
        {
            return;
        }

        try
        {
            RemoveReparsePoints(Root);
            DeleteDirectoryWithBoundedRetries(Root);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ScenarioWorkspace cleanup: failed to remove temporary workspace '{Root}': {exception.Message}");
        }
    }

    private static void RemoveReparsePoints(string root)
    {
        foreach (var path in EnumeratePaths(root).OrderByDescending(path => path.Length))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            File.SetAttributes(path, FileAttributes.Normal);
            if (Directory.Exists(path))
            {
                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Deletes the directory tree, retrying for a bounded window when a file is still transiently locked (for
    /// example immediately after killing a process whose handles have not yet released) instead of failing on the
    /// first attempt or retrying forever.
    /// </summary>
    private static void DeleteDirectoryWithBoundedRetries(string root)
    {
        foreach (var path in EnumeratePaths(root))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        var deadline = DateTime.UtcNow + WorkspaceCleanupTimeout;
        while (true)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(WorkspaceCleanupPollInterval);
            }
        }
    }

    private static string Normalize(string value) =>
        AnsiEscape.Replace(value.Replace("\r\n", "\n"), "");

    private ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyDictionary<string, string?>? environment,
        string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory ?? Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (executable == "git")
        {
            startInfo.Environment["GIT_AUTHOR_NAME"] = "BlaXquad Acceptance";
            startInfo.Environment["GIT_AUTHOR_EMAIL"] = "acceptance@example.invalid";
            startInfo.Environment["GIT_COMMITTER_NAME"] = "BlaXquad Acceptance";
            startInfo.Environment["GIT_COMMITTER_EMAIL"] = "acceptance@example.invalid";
        }
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }
        return startInfo;
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "squad.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>
    /// Resolves an exact executable from a specific test-output publication directory (for
    /// example the production-like "squad-tools" or the provider-free
    /// "squad-tools-backend-spec"). Never resolves from PATH or a checkout build output.
    /// </summary>
    private static string ResolveTool(string toolName, string publicationDirectoryName)
    {
        if (OperatingSystem.IsWindows())
        {
            toolName += ".exe";
        }
        return Path.Combine(AppContext.BaseDirectory, publicationDirectoryName, toolName);
    }

    private static IEnumerable<string> EnumeratePaths(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            yield return path;
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var child in EnumeratePaths(path))
                {
                    yield return child;
                }
            }
        }
    }

    private static void AssertSuccessful(CommandResult result) => result.EnsureSuccess();
}



