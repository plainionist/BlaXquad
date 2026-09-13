using squad.Process;
using squad.Application;
using squad.AgentProvider.Abstractions;
using squad.Hosting.Abstractions;
using squad.HeadquarterTools.Issues;
using squad.HeadquarterTools.History;
using squad.Workspaces;
using squad.Runtime;
using squad.Runtime.Control;
using squadHQ.Logging;
using Microsoft.Extensions.Logging;

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
            var layout = ProjectLayout.Create(root);

            if (!HeadquartersLease.TryAcquire(layout.WorkingDir, out var headquartersLease))
            {
                Fail($"A Headquarters instance is already running for {layout.WorkingDir}.");
                return;
            }

            // The launch owns exactly one diagnostic log file, created only once the Headquarters lease is held so
            // a rejected competing launch never creates a second one, and only before any provider/hosting plug-in
            // is loaded or the workspace is touched, so every later launch-boundary failure is captured in it.
            using var launchLogging = LaunchLogging.Start(layout);
            var logger = launchLogging.Logger;

            Headquarters? headquarters = null;
            using var consoleCancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler? cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                consoleCancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                IAgentProviderFactory agentProviderFactory;

                try
                {
                    agentProviderFactory = ProviderLoader.Load(providerDescriptor ?? DefaultProviderDescriptor());
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Provider selection failed.");
                    throw;
                }

                var viewModel = new SquadViewModel();
                var issueCatalog = new WorkspaceIssueCatalog(layout.WorkingDir);
                var gitHistoryTool = new GitHistoryTool(layout.WorkingDir);
                HostingRuntime hostingRuntime;

                try
                {
                    // An explicit "--hosting" descriptor and the packaged default (Photino) both load their factory at
                    // process startup through the same HostingLoader, exactly like "--provider" and its default. squad-hq
                    // has no compile-time dependency on either concrete hosting assembly.
                    hostingRuntime = HostingLoader.Load(hostingDescriptor ?? DefaultHostingDescriptor())
                        .Create(new HostingContext(layout.WorkingDir, viewModel, issueCatalog, gitHistoryTool));
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Hosting selection failed.");
                    throw;
                }

                var launchPreparer = new LaunchPreparer(layout, continueLaunch);

                headquarters = Headquarters.Create(
                    launchPreparer,
                    agentProviderFactory,
                    hostingRuntime.WindowHost,
                    hostingRuntime.SleepInhibitor,
                    viewModel,
                    gitHistoryTool,
                    headquartersLease: headquartersLease!,
                    launchLogging.LoggerFactory);
                headquartersLease = null;

                try
                {
                    headquarters.RunAsync(consoleCancellation.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (consoleCancellation.IsCancellationRequested)
                {
                }
                catch (AgentBackendTerminalFailureException exception)
                {
                    logger.LogError(exception, "Provider failed.");
                    Fail($"{Red}Error:{Reset} Provider failed: {DescribeFailure(exception)}");
                }
                catch (HandoffPumpTerminalFailureException exception)
                {
                    logger.LogError(exception, "Handoff delivery failed.");
                    Fail($"{Red}Error:{Reset} Handoff delivery failed: {DescribeFailure(exception)}");
                }
                catch (WorkspacePreparationException exception)
                {
                    logger.LogError(exception, "Workspace preparation failed.");
                    Fail($"{Red}Error:{Reset} {exception.Message}");
                }
                catch (Exception exception) when (exception is not CliExitException)
                {
                    logger.LogError(exception, "Provider startup failed.");
                    Fail($"{Red}Error:{Reset} Provider startup failed: {DescribeFailure(exception)}");
                }
            }
            catch (CliExitException)
            {
                // Already logged either just above (Fail() call sites) or in the provider/hosting selection catches
                // above; rethrow unchanged so the existing standard-error text and exit code are unaffected.
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Launch failed unexpectedly.");
                throw;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;

                if (headquarters is null && headquartersLease is not null)
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
