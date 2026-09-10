using squad.AgentProvider.Abstractions;
using squad.Process;
using squad.Configuration;
using squad.Handoffs;
using squad.Handoffs.Delivery;
using squad.Application;
using squad.Photino;
using squad.Stdio;
using squad.Ui.Abstractions;
using squad.Workspaces;
using squad.Host.Control;
using squad.Host.Runtime;

namespace squadHQ.Commands;

static class Launch
{
    private const string DefaultProviderAssemblyName = "squad.CopilotSdk.dll";
    private const string DefaultProviderTypeName = "squad.CopilotSdk.CopilotSdkAgentProviderFactory";

    public static int Run(string[] args)
    {
        const string Red = "\u001b[0;31m";
        const string Reset = "\u001b[0m";

        var (providerDescriptor, providerRemaining) = ProviderOption.Extract(args);
        var (uiMode, remaining) = UiOption.Extract(providerRemaining);

        switch (remaining.ElementAtOrDefault(0))
        {
            case "--continue":
                RunMain(remaining.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory(), continueLaunch: true, providerDescriptor, uiMode);
                return 0;
            default:
                RunMain(remaining.ElementAtOrDefault(0) ?? Directory.GetCurrentDirectory(), continueLaunch: false, providerDescriptor, uiMode);
                return 0;
        }

        void Fail(string message) => throw new CliExitException(1, message);

        static string DescribeFailure(Exception exception) =>
            exception is AggregateException aggregate
                ? string.Join(" ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
                : exception.Message;

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

        void RunMain(string root, bool continueLaunch, ProviderDescriptor? providerDescriptor, UiMode uiMode)
        {
            var agentProviderFactory = ProviderLoader.Load(providerDescriptor ?? DefaultProviderDescriptor());
            var context = BuildContext(root);
            context.ContinueLaunch = continueLaunch;
            HostLease? hostLease = HostLease.Acquire(context.WorkingDir);
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
                var runtime = Create(context.WorkingDir, viewModel, agentProviderFactory, uiMode);
                var startupPlan = SquadStartupPlanFactory.ForWorkspace(
                    context,
                    preparer,
                    prepareContextAsync: async cancellationToken =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!ExecutableLocator.Exists("git"))
                        {
                            Fail($"{Red}Error:{Reset} 'git' is required but not installed.");
                        }
                        await preparer.InitializeGitRepoAsync(context, cancellationToken);
                        await preparer.EnsureRuntimeGitExcludesAsync(context, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        PrepareContext(context);
                        return BuildBackendContext(context);
                    });

                application = SquadApplication.Create(
                    startupPlan,
                    runtime.AgentProviderFactory,
                    handoffPumpFactory: notifier => new InProcessHandoffPoller(
                        () => context.Roles.Select(r => new RoleRow(r.Role, r.WorktreeName, r.WorktreePath, r.DisplayName, r.ReceiveMode)).ToArray(),
                        notifier,
                        LogHandoff),
                    runtime.WindowHost,
                    runtime.SleepInhibitor,
                    viewModel,
                    hostLease: hostLease!);
                hostLease = null;
                try
                {
                    application.RunAsync(consoleCancellation.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (consoleCancellation.IsCancellationRequested)
                {
                }
                catch (AgentBackendTerminalFailureException exception)
                {
                    Fail($"{Red}Error:{Reset} Provider failed: {DescribeFailure(exception)}");
                }
                catch (HandoffPumpTerminalFailureException exception)
                {
                    Fail($"{Red}Error:{Reset} Handoff delivery failed: {DescribeFailure(exception)}");
                }
                catch (Exception exception) when (exception is not CliExitException)
                {
                    Fail($"{Red}Error:{Reset} Provider startup failed: {DescribeFailure(exception)}");
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                if (application is null && hostLease is not null)
                {
                    hostLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }

    private static RuntimeMode Create(string workingDirectory, ISquadUi ui, IAgentProviderFactory agentProviderFactory, UiMode uiMode) =>
        new(
            agentProviderFactory,
            uiMode == UiMode.Stdio ? new StdioWindowHost(ui) : new PhotinoWindowHost(ui, workingDirectory),
            new SleepInhibitor());

    // Built from data strings only (no squad.CopilotSdk source or assembly reference) so squad-hq stays
    // free of a compile-time dependency on the default provider while still launching with it by default.
    private static ProviderDescriptor DefaultProviderDescriptor() =>
        new(Path.Combine(AppContext.BaseDirectory, DefaultProviderAssemblyName), DefaultProviderTypeName);


    private static string InitialInstruction(string role) =>
        "Read blaxquad/constitution.prompt, then read every file it refers to recursively, and obey all of those instructions.\n" +
        $"Read blaxquad/roles/{role}.prompt, then read every file it refers to recursively, and follow all of those instructions.\n";
}



