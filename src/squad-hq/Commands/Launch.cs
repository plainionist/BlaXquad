using squad.Process;
using squad.Handoffs.Delivery;
using squad.Application;
using squad.Hosting.Abstractions;
using squad.Issues;
using squad.Photino;
using squad.Stdio;
using squad.Workspaces;
using squad.Host.Control;
using squad.Host.Runtime;

namespace squadHQ.Commands;

static class Launch
{
    private const string DefaultProviderAssemblyName = "squad.AgentProvider.CopilotSdk.dll";
    private const string DefaultProviderTypeName = "squad.AgentProvider.CopilotSdk.CopilotSdkAgentProviderFactory";

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

        void RunMain(string root, bool continueLaunch, ProviderDescriptor? providerDescriptor, UiMode uiMode)
        {
            var agentProviderFactory = ProviderLoader.Load(providerDescriptor ?? DefaultProviderDescriptor());
            var layout = ProjectLayout.Create(root);
            HostLease? hostLease = HostLease.Acquire(layout.WorkingDir);
            SquadApplication? application = null;
            using var consoleCancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler? cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                consoleCancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                var viewModel = new SquadViewModel();
                var issueCatalog = new WorkspaceIssueCatalog(layout.WorkingDir);
                IWindowHost windowHost = uiMode == UiMode.Stdio
                    ? new StdioWindowHost(viewModel, issueCatalog)
                    : new PhotinoWindowHost(viewModel, issueCatalog, layout.WorkingDir);
                var sleepInhibitor = new SleepInhibitor();
                var launchPreparer = new LaunchPreparer(layout, continueLaunch);

                application = SquadApplication.Create(
                    launchPreparer,
                    agentProviderFactory,
                    windowHost,
                    sleepInhibitor,
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
                catch (WorkspacePreparationException exception)
                {
                    Fail($"{Red}Error:{Reset} {exception.Message}");
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

    // Built from data strings only (no squad.AgentProvider.CopilotSdk source or assembly reference) so squad-hq stays
    // free of a compile-time dependency on the default provider while still launching with it by default.
    private static ProviderDescriptor DefaultProviderDescriptor() =>
        new(Path.Combine(AppContext.BaseDirectory, DefaultProviderAssemblyName), DefaultProviderTypeName);
}



