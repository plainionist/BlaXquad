using squad.Process;
using squad.Application;
using squad.Hosting.Abstractions;
using squad.Issues;
using squad.Workspaces;
using squad.Runtime;
using squad.Runtime.Control;

namespace squadHQ.Commands;

static class Launch
{
    private const string DefaultProviderAssemblyName = "squad.AgentProvider.CopilotSdk.dll";
    private const string DefaultProviderTypeName = "squad.AgentProvider.CopilotSdk.CopilotSdkAgentProviderFactory";
    private const string DefaultHostingAssemblyName = "squad.Hosting.Photino.dll";
    private const string DefaultHostingTypeName = "squad.Hosting.Photino.PhotinoHostingFactory";

    public static int Run(string[] args)
    {
        const string Red = "\u001b[0;31m";
        const string Reset = "\u001b[0m";

        var (providerDescriptor, providerRemaining) = ProviderOption.Extract(args);
        var (hostingDescriptor, remaining) = HostingOption.Extract(providerRemaining);

        switch (remaining.ElementAtOrDefault(0))
        {
            case "--continue":
                RunMain(remaining.ElementAtOrDefault(1) ?? Directory.GetCurrentDirectory(), continueLaunch: true, providerDescriptor, hostingDescriptor);
                return 0;
            default:
                RunMain(remaining.ElementAtOrDefault(0) ?? Directory.GetCurrentDirectory(), continueLaunch: false, providerDescriptor, hostingDescriptor);
                return 0;
        }

        void Fail(string message) => throw new CliExitException(1, message);

        static string DescribeFailure(Exception exception) =>
            exception is AggregateException aggregate
                ? string.Join(" ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
                : exception.Message;

        void RunMain(string root, bool continueLaunch, ProviderDescriptor? providerDescriptor, HostingDescriptor? hostingDescriptor)
        {
            var agentProviderFactory = ProviderLoader.Load(providerDescriptor ?? DefaultProviderDescriptor());
            var layout = ProjectLayout.Create(root);
            if (!HeadquartersLease.TryAcquire(layout.WorkingDir, out var headquartersLease))
            {
                Fail($"A Headquarters instance is already running for {layout.WorkingDir}.");
                return;
            }
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
                // An explicit "--hosting" descriptor and the packaged default (Photino) both load their factory at
                // process startup through the same HostingLoader, exactly like "--provider" and its default. squad-hq
                // has no compile-time dependency on either concrete hosting assembly.
                var hostingRuntime = HostingLoader.Load(hostingDescriptor ?? DefaultHostingDescriptor())
                    .Create(new HostingContext(layout.WorkingDir, viewModel, issueCatalog));
                var launchPreparer = new LaunchPreparer(layout, continueLaunch);

                application = SquadApplication.Create(
                    launchPreparer,
                    agentProviderFactory,
                    hostingRuntime.WindowHost,
                    hostingRuntime.SleepInhibitor,
                    viewModel,
                    headquartersLease: headquartersLease!);
                headquartersLease = null;
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
                if (application is null && headquartersLease is not null)
                {
                    headquartersLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }

    // Built from data strings only (no squad.AgentProvider.CopilotSdk source or assembly reference) so squad-hq stays
    // free of a compile-time dependency on the default provider while still launching with it by default.
    private static ProviderDescriptor DefaultProviderDescriptor() =>
        new(Path.Combine(AppContext.BaseDirectory, DefaultProviderAssemblyName), DefaultProviderTypeName);

    // Built from data strings only (no squad.Hosting.Photino source or assembly reference) so squad-hq stays free
    // of a compile-time dependency on the packaged default hosting plug-in while still launching with it by default.
    private static HostingDescriptor DefaultHostingDescriptor() =>
        new(Path.Combine(AppContext.BaseDirectory, DefaultHostingAssemblyName), DefaultHostingTypeName);
}



