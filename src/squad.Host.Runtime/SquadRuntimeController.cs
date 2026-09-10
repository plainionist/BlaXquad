using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application;
using squad.Handoffs.Delivery;

namespace squad.Host.Runtime;

/// <summary>
/// Owns the current backend generation and coordinates session registration, handoff recovery, handoff polling,
/// and failure-collecting teardown under <see cref="SessionRegistry"/> lifecycle transitions. Process resources
/// such as the window, host lease, workspace, and sleep inhibitor remain owned by <see cref="SquadApplication"/>.
/// </summary>
internal sealed class SquadRuntimeController
{
    private readonly SessionRegistry mySessionRegistry;
    private readonly SquadViewModel myViewModel;
    private readonly InProcessHandoffPoller myHandoffPump;
    private readonly SessionGeneration mySessionGeneration;
    private bool myHandoffStarted;

    public SquadRuntimeController(
        SessionRegistry sessionRegistry,
        IAgentBackend agentBackend,
        SquadViewModel viewModel,
        InProcessHandoffPoller handoffPump,
        CancellationToken stoppingToken)
    {
        mySessionRegistry = sessionRegistry;
        myViewModel = viewModel;
        myHandoffPump = handoffPump;
        mySessionGeneration = new SessionGeneration(agentBackend, viewModel, stoppingToken);
    }

    public async Task StartAsync(Func<CancellationToken, Task> onSessionsStarted, CancellationToken cancellationToken)
    {
        using var transition = mySessionRegistry.BeginStarting();
        await mySessionGeneration.StartAsync(RegisterSessionAsync, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await onSessionsStarted(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await myHandoffPump.RecoverAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await myHandoffPump.StartAsync(cancellationToken);
        myHandoffStarted = true;
        transition.Commit();
    }

    private Task RegisterSessionAsync(IAgentSession session)
    {
        mySessionRegistry.Register(session);
        myViewModel.RegisterSession(session);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Exception>> StopAsync()
    {
        var failures = new List<Exception>();
        using var transition = mySessionRegistry.BeginStopping();
        await AttemptAsync(myViewModel.StopAsync, failures);
        if (myHandoffStarted)
        {
            await AttemptAsync(() => myHandoffPump.StopAsync(), failures);
        }
        failures.AddRange(await mySessionGeneration.TeardownAsync());
        transition.Commit();
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
