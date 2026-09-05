using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application;
using squad.Handoffs;

namespace squadHQ.Commands;

/// <summary>
/// The sole owner of the current backend generation. It coordinates constructing and registering a
/// <see cref="SessionGeneration"/>, recovering and starting handoff production for it, and its ordered,
/// failure-collecting teardown, all under one <see cref="SessionRegistry"/> lifecycle transition. It owns no
/// lifecycle phase or command-admission state of its own - those stay exclusively in <see cref="SessionRegistry"/> -
/// and no window, host-lease, workspace-preparation, or sleep-inhibition lifetime, which stay with
/// <c>SquadApplication</c>. A focused callback lets the window host be notified at the point it needs, between
/// session registration and handoff recovery, without transferring window ownership here.
/// </summary>
internal sealed class SquadRuntimeController
{
    private readonly SessionRegistry mySessionRegistry;
    private readonly SquadViewModel myViewModel;
    private readonly IHandoffPump myHandoffPump;
    private readonly SessionGeneration mySessionGeneration;
    private bool myHandoffStarted;

    public SquadRuntimeController(
        SessionRegistry sessionRegistry,
        IAgentBackend agentBackend,
        Action<AgentEvent> eventSink,
        SquadViewModel viewModel,
        IHandoffPump handoffPump,
        CancellationToken stoppingToken)
    {
        mySessionRegistry = sessionRegistry;
        myViewModel = viewModel;
        myHandoffPump = handoffPump;
        mySessionGeneration = new SessionGeneration(agentBackend, eventSink, viewModel, stoppingToken);
    }

    public IReadOnlyDictionary<string, IAgentSession> Sessions => mySessionGeneration.Sessions;

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
            await AttemptAsync(() => myHandoffPump.StopAsync(), failures);
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
