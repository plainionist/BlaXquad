using squad.AgentProvider.Abstractions;
using squad.Application;
using squad.Handoffs.Delivery;
using squad.Hosting.Abstractions;

namespace squad.Runtime;

/// <summary>
/// Owns the current backend generation and coordinates session registration, handoff recovery, handoff polling,
/// and failure-collecting teardown. Process resources such as the window, host lease, workspace, and sleep
/// inhibitor remain owned by <see cref="SquadApplication"/>.
/// </summary>
internal sealed class SquadRuntimeController
{
    private readonly IWindowHost myWindowHost;
    private readonly SquadViewModel myViewModel;
    private readonly InProcessHandoffPoller myHandoffPump;
    private readonly SessionGeneration mySessionGeneration;
    private bool myHandoffStarted;

    public SquadRuntimeController(
        IWindowHost windowHost,
        IAgentBackend agentBackend,
        SquadViewModel viewModel,
        InProcessHandoffPoller handoffPump,
        CancellationToken stoppingToken)
    {
        myWindowHost = windowHost;
        myViewModel = viewModel;
        myHandoffPump = handoffPump;
        mySessionGeneration = new SessionGeneration(agentBackend, viewModel, stoppingToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await mySessionGeneration.StartAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await myWindowHost.SessionsStartedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await myHandoffPump.RecoverAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await myHandoffPump.StartAsync(cancellationToken);
        myHandoffStarted = true;
    }

    public async Task<IReadOnlyList<Exception>> StopAsync()
    {
        var failures = new List<Exception>();
        await AttemptAsync(myViewModel.StopAsync, failures);
        if (myHandoffStarted)
        {
            await AttemptAsync(() => myHandoffPump.StopAsync(), failures);
        }
        failures.AddRange(await mySessionGeneration.TeardownAsync());
        return failures;
    }

    private static async Task AttemptAsync(Func<Task> action, ICollection<Exception> failures)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
