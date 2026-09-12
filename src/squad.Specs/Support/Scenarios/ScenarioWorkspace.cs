using squad.Specs.Support.Processes;

namespace squad.Specs.Support.Scenarios;

public sealed class ScenarioWorkspace : IDisposable
{
    private static readonly TimeSpan WorkspaceCleanupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WorkspaceCleanupPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly object ArtifactPreparationLock = new();
    private static readonly HashSet<string> PreparedArtifactTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> myValues = new(StringComparer.Ordinal);
    private readonly ScenarioProcessRunner myProcessRunner = new();
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
    /// Deletes a file previously written into this workspace (for example a configuration or constitution file),
    /// so a specification can arrange a missing-file workspace failure as one semantic operation.
    /// </summary>
    public void DeleteFile(string relativePath) =>
        File.Delete(Path.Combine(Root, Path.Combine(relativePath.Split('/'))));

    /// <summary>
    /// Rewrites `blaxquad/squad.json` back to the healthy configuration <see cref="ConfigureProject"/> or
    /// <see cref="ConfigureRoles(string[])"/> already established for every currently configured role, for a
    /// specification proving a previously reported workspace failure (for example a missing or malformed
    /// configuration file) no longer blocks a subsequent launch once the underlying problem is fixed.
    /// </summary>
    public void RestoreProjectConfiguration() =>
        WriteSquadConfiguration(
            myRoleWorktrees.Keys.FirstOrDefault(),
            myRoleWorktrees.Keys.Select(role => (Role: role, ReceiveMode: (string?)null)).ToList());

    /// <summary>
    /// Declares <paramref name="sharedRelativePath"/> as a `sharedWorktreePaths` entry in `blaxquad/squad.json`
    /// for every role already configured by <see cref="ConfigureRoles(string[])"/>, preserving each role's worktree
    /// mapping, for a specification that arranges an unsafe (non-empty) shared worktree path as a semantic
    /// workspace operation.
    /// </summary>
    public void ConfigureSharedWorktreePath(string sharedRelativePath)
    {
        var leader = myRoleWorktrees.Keys.First();
        var members = myRoleWorktrees.Keys
            .Select(role => (Name: role, Role: role, Worktree: role, ReceiveMode: (string?)null))
            .ToList();
        WriteFile("blaxquad/squad.json", BuildSquadConfigurationJson(leader, members, [sharedRelativePath]));
    }

    /// <summary>
    /// Rewrites `blaxquad/squad.json` for every role already configured by <see cref="ConfigureRoles(string[])"/>,
    /// adding the given raw JSON value under the given top-level field name (an array literal, or a malformed one
    /// such as "[]"), preserving each role's worktree mapping - generic to any current or future per-tool launch
    /// command field, so a specification can arrange one as a single semantic workspace operation without a
    /// bespoke method per tool.
    /// </summary>
    public void ConfigureToolCommand(string fieldName, string commandJson)
    {
        var leader = myRoleWorktrees.Keys.First();
        var members = myRoleWorktrees.Keys
            .Select(role => (Name: role, Role: role, Worktree: role, ReceiveMode: (string?)null))
            .ToList();
        WriteFile(
            "blaxquad/squad.json",
            BuildSquadConfigurationJson(
                leader,
                members,
                additionalPropertyName: fieldName,
                additionalPropertyJson: commandJson));
    }

    /// <summary>
    /// Creates a non-empty directory at <paramref name="relativePath"/> inside the given role's own worktree
    /// (recorded by <see cref="ConfigureProject"/>), so a real launch attempting to replace it with a shared
    /// worktree path link genuinely finds pre-existing content it must not silently discard.
    /// </summary>
    public void SeedNonEmptyDirectory(string role, string relativePath)
    {
        var target = Path.Combine(myRoleWorktrees[role], Path.Combine(relativePath.Split('/')));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "existing.txt"), "pre-existing content\n");
    }

    /// <summary>
    /// Copies the built, provider-free backend-spec squad-hq deployment (see
    /// <see cref="BackendSpecSquadHqExecutablePath"/>) into a directory owned by this workspace with its required
    /// "squad" helper script removed, so a specification can prove a missing helper script is detected through a
    /// real deployment - never by deleting the shared, concurrently used test-output helper other scenarios still
    /// depend on. Returns the copied deployment's own squad-hq executable path.
    /// </summary>
    public string CreateBackendSpecDeploymentMissingHelperScript()
    {
        var source = Path.GetDirectoryName(BackendSpecSquadHqExecutablePath)!;
        var destination = PathInWorkspace("squad-hq-deployment-without-helper");
        CopyDirectoryRecursive(source, destination);

        var helperName = OperatingSystem.IsWindows() ? "squad.exe" : "squad";
        var helperPath = Path.Combine(destination, helperName);
        if (File.Exists(helperPath))
        {
            File.Delete(helperPath);
        }

        var executableName = OperatingSystem.IsWindows() ? "squad-hq.exe" : "squad-hq";
        return Path.Combine(destination, executableName);
    }

    /// <summary>
    /// The stdio hosting plug-in assembly published standalone into its own directory (see the
    /// "PublishHostingStdioFixture" MSBuild target), complete with a real "squad.Hosting.Stdio.deps.json" and its
    /// own copies of every contract assembly whose types cross the hosting boundary
    /// (<c>squad.Hosting.Abstractions</c>, <c>squad.Ui.Abstractions</c>, <c>squad.AgentProvider.Abstractions</c>)
    /// plus its remaining private dependencies (<c>squad.Ui.Protocol</c>) - a layout
    /// <see cref="System.Runtime.Loader.AssemblyDependencyResolver"/> genuinely resolves, unlike an ad hoc copy of
    /// select DLLs with no dependency manifest. Proves the hosting loader still unifies those duplicated contract
    /// assemblies with headquarters' own copies rather than loading the local copies sitting right beside the
    /// plug-in. Returns the deployed plug-in assembly's path.
    /// </summary>
    public string HostingFixtureDeploymentWithDuplicateContractsPath
    {
        get
        {
            EnsureArtifact("PublishHostingStdioFixture");
            return Path.Combine(AppContext.BaseDirectory, "hosting-fixture-duplicate-contracts", "squad.Hosting.Stdio.dll");
        }
    }

    /// <summary>
    /// The packaged default Photino hosting plug-in assembly as it actually ships inside the production-like
    /// "squad-tools" publication (see the "IncludePhotinoHostingInPublishOutput" MSBuild target) - used to prove
    /// that an explicit "--hosting" descriptor resolves the very same packaged plug-in squad-hq already resolves
    /// by default when "--hosting" is omitted, rather than a second, separately built copy.
    /// </summary>
    public string SquadToolsPhotinoHostingAssemblyPath =>
        Path.Combine(PublishedToolsDirectory, "squad.Hosting.Photino.dll");

    public string PublishedToolsDirectory
    {
        get
        {
            EnsureArtifact("PublishSquadTools");
            return Path.Combine(AppContext.BaseDirectory, "squad-tools");
        }
    }

    public string NeutralPublishedToolsDirectory
    {
        get
        {
            EnsureArtifact("PublishNeutralSquadTools");
            return Path.Combine(AppContext.BaseDirectory, "squad-tools-neutral");
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectoryRecursive(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }


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
        var executable = ResolveTool(toolName, "squad-tools-backend-spec");
        return Run(executable, arguments ?? [], environment, workingDirectory);
    }

    public CommandResult RunPublishedTool(
        string toolName,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var executable = Path.Combine(
            PublishedToolsDirectory,
            OperatingSystem.IsWindows() ? toolName + ".exe" : toolName);
        return Run(executable, arguments ?? [], environment, workingDirectory);
    }

    /// <summary>
    /// The exact executable path used by <see cref="RunBackendSpecSquadHq"/>, exposed so
    /// specifications can prove a command ran that precise staged build rather than PATH, a
    /// checkout binary, or the production-like squad-tools publication.
    /// </summary>
    public string BackendSpecSquadHqExecutablePath => ResolveTool("squad-hq", "squad-tools-backend-spec");

    /// <summary>
    /// Runs the exact built, provider-free squad-hq used by backend specifications, never the
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
    /// Creates a uniquely rooted, configured Git project with one linked worktree per role, optionally declaring
    /// each role's `receiveMode` (a null entry omits the field so the tool applies its own default), and records
    /// each role's worktree path behind this workspace (see <see cref="RunRoleTool"/>). Lets scenarios that arrange
    /// an unsupported, missing, or batch receive mode do so as one semantic workspace operation instead of
    /// serializing configuration in step definitions.
    /// </summary>
    public IReadOnlyDictionary<string, string> ConfigureProject(params string[] roles) =>
        ConfigureProjectWithLeader(roles.Length == 0 ? "" : roles[0], roles);

    public IReadOnlyDictionary<string, string> ConfigureProjectWithLeader(string leader, params string[] roles) =>
        ConfigureProject(roles.Select(role => (Role: role, ReceiveMode: (string?)null)).ToList(), leader);

    public IReadOnlyDictionary<string, string> ConfigureProject(IReadOnlyList<(string Role, string? ReceiveMode)> roles) =>
        ConfigureProject(roles, roles.Count == 0 ? "" : roles[0].Role);

    public IReadOnlyDictionary<string, string> ConfigureProject(IReadOnlyList<(string Role, string? ReceiveMode)> roles, string? leader)
    {
        if (roles.Count == 0)
        {
            throw new ArgumentException("At least one role is required.", nameof(roles));
        }

        InitializeGitRepository();
        WriteFile("blaxquad/constitution.prompt", "Follow the project constitution.\n");

        foreach (var (role, _) in roles)
        {
            WriteFile($"blaxquad/roles/{role}.prompt", $"Act as the {role}.\n");
            var worktreePath = PathInWorkspace(".worktrees", role);
            AssertSuccessful(RunGit("worktree", "add", "--quiet", "-b", $"squad-{role}", worktreePath));
            myRoleWorktrees[role] = worktreePath;
        }

        WriteSquadConfiguration(leader, roles);

        return myRoleWorktrees;
    }

    public void SetLeader(string? leader, params string[] roles) =>
        WriteSquadConfiguration(leader, roles.Select(role => (Role: role, ReceiveMode: (string?)null)).ToList());

    private void WriteSquadConfiguration(string? leader, IReadOnlyList<(string Role, string? ReceiveMode)> roles)
    {
        var members = roles
            .Select(entry => (Name: entry.Role, Role: entry.Role, Worktree: entry.Role, ReceiveMode: entry.ReceiveMode))
            .ToList();
        WriteFile("blaxquad/squad.json", BuildSquadConfigurationJson(leader, members));
    }

    /// <summary>
    /// Rewrites `blaxquad/squad.json` for every role already established by <see cref="ConfigureProject"/> to
    /// declare an explicit receive mode (including an empty string) for the given role, preserving every other
    /// configured role's worktree mapping and default receive mode unchanged. Lets scenarios arrange an
    /// unsupported or missing receive mode after Background configuration as one semantic workspace operation,
    /// without repeating Git project bootstrap.
    /// </summary>
    public void ConfigureReceiveMode(string role, string receiveMode)
    {
        var members = myRoleWorktrees.Keys
            .Select(configuredRole => (
                Name: configuredRole,
                Role: configuredRole,
                Worktree: configuredRole,
                ReceiveMode: configuredRole == role ? receiveMode : (string?)null))
            .ToList();
        WriteFile("blaxquad/squad.json", BuildSquadConfigurationJson(role, members));
    }

    /// <summary>
    /// Builds the complete schema-version-2 `blaxquad/squad.json` document for the given members, deriving the
    /// "roles" name catalog from each distinct <c>Role</c> referenced by a member so a shared-role scenario (see
    /// <see cref="ConfigureProjectWithSharedRole"/>) still emits one role catalog entry even though several members
    /// reference it.
    /// </summary>
    private static string BuildSquadConfigurationJson(
        string? leader,
        IReadOnlyList<(string Name, string Role, string Worktree, string? ReceiveMode)> members,
        IReadOnlyList<string>? sharedWorktreePaths = null,
        string? additionalPropertyName = null,
        string? additionalPropertyJson = null)
    {
        var rolesJson = string.Join(", ", members
            .Select(member => member.Role)
            .Distinct(StringComparer.Ordinal)
            .Select(role => $"\"{role}\""));
        var membersJson = string.Join(",\n", members.Select(member =>
            MemberJson(member.Name, member.Role, member.Worktree, member.ReceiveMode)));
        var leaderLine = leader is null ? "" : $$"""  "leader": "{{leader}}",{{"\n"}}""";
        var sharedWorktreePathsLine = sharedWorktreePaths is null || sharedWorktreePaths.Count == 0
            ? ""
            : $$"""  "sharedWorktreePaths": [{{string.Join(", ", sharedWorktreePaths.Select(path => $"\"{path}\""))}}],{{"\n"}}""";
        var additionalPropertyLine = additionalPropertyName is null
            ? ""
            : $$"""  "{{additionalPropertyName}}": {{additionalPropertyJson}},{{"\n"}}""";
        return $$"""
            {
              "schemaVersion": 2,
            {{leaderLine}}{{sharedWorktreePathsLine}}{{additionalPropertyLine}}  "roles": [{{rolesJson}}],
              "members": [
            {{membersJson}}
              ]
            }
            """ + "\n";
    }

    private static string MemberJson(string name, string role, string worktree, string? receiveMode) => receiveMode is null
        ? $$"""    { "name": "{{name}}", "role": "{{role}}", "worktree": "{{worktree}}", "agent": {} }"""
        : $$"""    { "name": "{{name}}", "role": "{{role}}", "worktree": "{{worktree}}", "receiveMode": "{{receiveMode}}", "agent": {} }""";

    /// <summary>
    /// Creates a Git project where every named role maps onto the same repository root (a "master" worktree)
    /// instead of <see cref="ConfigureProject(string[])"/>'s one-worktree-per-role layout, for scenarios that
    /// arrange an ambiguous current-worktree identity as a semantic workspace operation.
    /// </summary>
    public void ConfigureProjectWithRolesSharingWorktree(params string[] roles)
    {
        InitializeGitRepository();
        var members = roles
            .Select(role => (Name: role, Role: role, Worktree: "master", ReceiveMode: (string?)null))
            .ToList();
        WriteFile("blaxquad/squad.json", BuildSquadConfigurationJson(roles[0], members));
    }

    /// <summary>
    /// Creates a uniquely rooted, configured Git project with a single reusable role - shared by every named
    /// member, each of which gets its own linked worktree - so a specification can prove two members referencing
    /// the same role get independent sessions and worktrees while both reading the same role prompt. Records each
    /// member's worktree path behind this workspace exactly like <see cref="ConfigureProject(string[])"/>, keyed by
    /// member name (not role name), so <see cref="RunRoleTool"/> and friends address members precisely.
    /// </summary>
    public IReadOnlyDictionary<string, string> ConfigureProjectWithSharedRole(string role, params string[] memberNames)
    {
        if (memberNames.Length == 0)
        {
            throw new ArgumentException("At least one member is required.", nameof(memberNames));
        }

        InitializeGitRepository();
        WriteFile("blaxquad/constitution.prompt", "Follow the project constitution.\n");
        WriteFile($"blaxquad/roles/{role}.prompt", $"Act as the {role}.\n");

        foreach (var memberName in memberNames)
        {
            var worktreePath = PathInWorkspace(".worktrees", memberName);
            AssertSuccessful(RunGit("worktree", "add", "--quiet", "-b", $"squad-{memberName}", worktreePath));
            myRoleWorktrees[memberName] = worktreePath;
        }

        var members = memberNames
            .Select(memberName => (Name: memberName, Role: role, Worktree: memberName, ReceiveMode: (string?)null))
            .ToList();
        WriteFile("blaxquad/squad.json", BuildSquadConfigurationJson(memberNames[0], members));

        return myRoleWorktrees;
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

    public System.Diagnostics.Process StartProcess(
        string executable,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null,
        bool redirectStandardInput = false) =>
        myProcessRunner.Start(executable, arguments, MergeGitIdentity(executable, environment), workingDirectory ?? Root, redirectStandardInput);

    /// <summary>
    /// Registers a process launched outside <see cref="StartProcess"/> (for example, through
    /// <see cref="CancellableChildProcess"/>) so this workspace's own emergency cleanup on <see cref="Dispose"/>
    /// still terminates it if a specification never reaches its own normal shutdown.
    /// </summary>
    public void TrackProcess(System.Diagnostics.Process process) => myProcessRunner.TrackProcess(process);

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
        LastResult = myProcessRunner.Run(executable, arguments, MergeGitIdentity(executable, environment), workingDirectory ?? Root);
        return LastResult;
    }

    /// <summary>
    /// Merges the fixture's fixed Git author/committer identity ahead of any explicit environment for a "git"
    /// invocation, so every commit this workspace or a role's worktree makes carries a stable, real identity
    /// instead of depending on the ambient environment. Any other executable's environment passes through
    /// unchanged.
    /// </summary>
    private static IReadOnlyDictionary<string, string?>? MergeGitIdentity(
        string executable, IReadOnlyDictionary<string, string?>? environment)
    {
        if (executable != "git")
        {
            return environment;
        }

        var merged = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_AUTHOR_NAME"] = "BlaXquad Acceptance",
            ["GIT_AUTHOR_EMAIL"] = "acceptance@example.invalid",
            ["GIT_COMMITTER_NAME"] = "BlaXquad Acceptance",
            ["GIT_COMMITTER_EMAIL"] = "acceptance@example.invalid",
        };
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                merged[name] = value;
            }
        }
        return merged;
    }

    /// <summary>
    /// Stops every process this workspace started and removes its temporary directory. Each step is bounded and
    /// isolated: a process that will not stop, or a directory entry that will not delete, is logged and skipped
    /// rather than left to hang or to throw out of <see cref="Dispose"/> - a cleanup failure here must never
    /// replace a scenario's real failure.
    /// </summary>
    public void Dispose()
    {
        myProcessRunner.Dispose();

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

    private void EnsureArtifact(string target)
    {
        var key = $"{AppContext.BaseDirectory}|{target}";
        lock (ArtifactPreparationLock)
        {
            if (PreparedArtifactTargets.Contains(key))
            {
                return;
            }

            var project = Path.Combine(RepositoryRootPath, "src", "squad.Specs", "squad.Specs.csproj");
            var result = Run(
                "dotnet",
                [
                    "msbuild",
                    project,
                    $"-target:{target}",
                    "-property:Configuration=Release",
                    $"-property:TargetDir={AppContext.BaseDirectory}",
                    "-nologo",
                    "-verbosity:quiet",
                ],
                workingDirectory: RepositoryRootPath);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to prepare specification artifact '{target}':{Environment.NewLine}{result.StdErr}");
            }
            PreparedArtifactTargets.Add(key);
        }
    }

    /// <summary>
    /// Resolves an exact executable from the staged backend-spec runtime. Never resolves from PATH or an arbitrary
    /// checkout build output.
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


