using global::squad.AgentProvider.Abstractions;
using global::squad.Agent;
using global::squad.Core;
using global::squad.Photino;

namespace squad_hq.Commands;

static class Launch
{
    public static int Run(string[] args)
    {
        const string Red = "\u001b[0;31m";
        const string Reset = "\u001b[0m";

        switch (args.ElementAtOrDefault(0))
        {
            case "--test-parse":
                TestParse(args.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory());
                return 0;
            case "--test-window-title":
                Console.WriteLine(RuntimeModeSelector.WindowTitle(args.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory()));
                return 0;
            case "--test-command-exists":
                Console.WriteLine(ProcessControl.CommandExists(args[1]) ? "available" : "unavailable");
                return 0;
            case "--test-launch-selection":
                TestLaunchSelection();
                return 0;
            case "--test-prepare-launch":
                TestPrepareLaunch(args.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory(), continueLaunch: false);
                return 0;
            case "--test-continue-launch":
                TestPrepareLaunch(args.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory(), continueLaunch: true);
                return 0;
            case "--test-packaged-ui":
                TestPackagedUi(args.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory());
                return 0;
            case "--continue":
                RunMain(args.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory(), continueLaunch: true);
                return 0;
            default:
                if (args.Length > 0 && RuntimeModeSelector.TryRunPrivateCommand(args[0].TrimStart('-'), args[1..], out var exitCode))
                    return exitCode;
                RunMain(args.ElementAtOrDefault(0) ?? Directory.GetCurrentDirectory(), continueLaunch: false);
                return 0;
        }

        void Fail(string message) => throw new CliExitException(1, message);

        IRuntimeModeFactory SelectFactory()
        {
            try
            {
                return RuntimeModeSelector.Select(Environment.GetEnvironmentVariable("BLAXQUAD_AGENT_BACKEND"));
            }
            catch (InvalidOperationException exception)
            {
                Fail(exception.Message);
                throw;
            }
        }

        Ctx BuildContext(string workingDirArgument)
        {
            var layout = ProjectLayout.Create(workingDirArgument);
            return new Ctx
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
                Roles = [],
            };
        }

        Ctx PrepareContext(Ctx context)
        {
            new WorkspacePreparer(Fail).Parse(context);
            return context;
        }

        AgentBackendContext BuildBackendContext(Ctx context)
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
                context.Roles.Select(role => new AgentRoleContext(
                    role.Role,
                    role.DisplayName,
                    role.WorktreePath,
                    InitialInstruction(role.Role),
                    role.Permissions,
                    role.Model,
                    role.Effort)).ToArray(),
                environment);
        }

        void TestParse(string root)
        {
            var context = PrepareContext(BuildContext(root));
            new WorkspacePreparer(Fail).PrepareWorkspace(context);
            foreach (var row in context.Roles)
            {
                Console.WriteLine(
                    $"{row.Role} {row.DisplayName} {row.WorktreePath} {row.ReceiveMode} " +
                    $"permissions={row.Permissions} model={row.Model ?? "default"} effort={row.Effort ?? "default"}");
            }
        }

        void TestPackagedUi(string root)
        {
            Environment.SetEnvironmentVariable("BLAXQUAD_PHOTINO_SMOKE", "1");
            var viewModel = new SquadViewModel();
            var windowHost = new PhotinoWindowHost(viewModel, root);
            try
            {
                windowHost.StartAsync().GetAwaiter().GetResult();
                windowHost.WaitForCloseAsync().GetAwaiter().GetResult();
                Console.WriteLine("ui.ready");
            }
            finally
            {
                windowHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
                viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        void TestPrepareLaunch(string root, bool continueLaunch)
        {
            var context = BuildContext(root);
            context.ContinueLaunch = continueLaunch;
            var preparer = new WorkspacePreparer(Fail);
            preparer.InitializeGitRepo(context);
            preparer.EnsureRuntimeGitExcludes(context);
            PrepareContext(context);
            preparer.PrepareWorkspace(context);
            preparer.PrepareConfiguredWorktreesForLaunchAsync(context, context.ContinueLaunch, CancellationToken.None).GetAwaiter().GetResult();
            preparer.PrepareHandoffDirs(context);
        }

        void TestLaunchSelection()
        {
            var factory = SelectFactory();
            var context = BuildContext(Directory.GetCurrentDirectory());
            var viewModel = new SquadViewModel();
            var runtime = RuntimeModeSelector.Create(factory, () => BuildBackendContext(context), viewModel);
            try
            {
                Console.WriteLine($"{factory.Name} {runtime.WindowHost.GetType().Name} {runtime.AgentBackend.GetType().Name}");
            }
            finally
            {
                runtime.WindowHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
                runtime.AgentBackend.DisposeAsync().AsTask().GetAwaiter().GetResult();
                runtime.SleepInhibitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
                viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        void RunMain(string root, bool continueLaunch)
        {
            var factory = SelectFactory();
            var context = BuildContext(root);
            context.ContinueLaunch = continueLaunch;
            IHostLease? hostLease = HostLease.Acquire(context.WorkingDir);
            SquadApplication? application = null;
            using var consoleCancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler? cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                consoleCancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            void LogHandoff(string[] parts)
            {
                Directory.CreateDirectory(context.StateDir);
                File.AppendAllText(context.HandoffLog, $"{Timestamps.Now()} {string.Join(" ", parts)}\n");
            }

            try
            {
                var preparer = new WorkspacePreparer(Fail);
                var viewModel = new SquadViewModel();
                var runtime = RuntimeModeSelector.Create(factory, () => BuildBackendContext(context), viewModel);
                var sessionRegistry = new SessionRegistry();
                var handoffPump = new InProcessHandoffPoller(
                    () => context.Roles.Select(r => new RoleRow(r.Role, r.WorktreeName, r.WorktreePath, r.DisplayName, r.ReceiveMode)).ToArray(),
                    new SessionRoleNotifier(sessionRegistry, viewModel),
                    LogHandoff);

                application = new SquadApplication(
                    context,
                    preparer,
                    runtime.AgentBackend,
                    handoffPump,
                    runtime.WindowHost,
                    runtime.SleepInhibitor,
                    _ => { },
                    viewModel,
                    hostLease: hostLease,
                    postLockPreparation: async cancellationToken =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!ProcessControl.CommandExists("git"))
                            Fail($"{Red}Error:{Reset} 'git' is required but not installed.");
                        await preparer.InitializeGitRepoAsync(context, cancellationToken);
                        await preparer.EnsureRuntimeGitExcludesAsync(context, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        PrepareContext(context);
                        await runtime.PrepareAsync(cancellationToken);
                    },
                    sessionRegistry);
                hostLease = null;
                try
                {
                    application.RunAsync(() => Task.CompletedTask, consoleCancellation.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (consoleCancellation.IsCancellationRequested)
                {
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                if (application is null && hostLease is not null)
                    hostLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }



    private static string InitialInstruction(string role) =>
        "Read blaxquad/constitution.prompt, then read every file it refers to recursively, and obey all of those instructions.\n" +
        $"Read blaxquad/roles/{role}.prompt, then read every file it refers to recursively, and follow all of those instructions.\n";
}



