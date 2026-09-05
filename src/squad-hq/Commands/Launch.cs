using squad.AgentProvider.Abstractions;
using squad.Process;
using squad.Configuration;
using squad.Handoffs;
using squad.Application;
using squad.Photino;
using squad.Ui.Abstractions;
using squad.CopilotSdk;

namespace squadHQ.Commands;

static class Launch
{
    public static int Run(string[] args)
    {
        const string Red = "\u001b[0;31m";
        const string Reset = "\u001b[0m";

        switch (args.ElementAtOrDefault(0))
        {
            case "--continue":
                RunMain(args.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory(), continueLaunch: true);
                return 0;
            default:
                RunMain(args.ElementAtOrDefault(0) ?? Directory.GetCurrentDirectory(), continueLaunch: false);
                return 0;
        }

        void Fail(string message) => throw new CliExitException(1, message);

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

        void RunMain(string root, bool continueLaunch)
        {
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
                var runtime = Create(() => BuildBackendContext(context), viewModel);
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
                    sessionRegistry,
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
                    });
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

    private static RuntimeMode Create(Func<AgentBackendContext> context, ISquadUi ui)
    {
        var factory = new CopilotSdkRuntimeModeFactory();
        var backend = factory.CreateBackend(context);
        return new RuntimeMode(
            backend,
            new PhotinoWindowHost(ui, context().WorkingDirectory),
            new SleepInhibitor(),
            cancellationToken => factory.PrepareAsync(context, cancellationToken));
    }


    private static string InitialInstruction(string role) =>
        "Read blaxquad/constitution.prompt, then read every file it refers to recursively, and obey all of those instructions.\n" +
        $"Read blaxquad/roles/{role}.prompt, then read every file it refers to recursively, and follow all of those instructions.\n";
}



