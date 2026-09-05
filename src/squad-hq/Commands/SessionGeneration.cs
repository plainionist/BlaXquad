using global::squad.AgentProvider.Abstractions;
using global::squad.AgentProvider.Abstractions.Agents;
using global::squad.Application;
using System.Runtime.ExceptionServices;

namespace squadHQ.Commands;

/// <summary>
/// The sole owner of one backend generation's runtime handle, registered-session projection, event/completion
/// observer tasks, and observer cancellation sources. It never calls <see cref="IAgentSession.DisposeAsync"/>
/// directly; session disposal is entirely the runtime owner's responsibility.
/// </summary>
internal sealed class SessionGeneration
{
    private readonly IAgentBackend myAgentBackend;
    private readonly Action<AgentEvent> myEventSink;
    private readonly SquadViewModel myViewModel;
    private readonly CancellationToken myStoppingToken;
    private readonly Dictionary<string, IAgentSession> mySessions = new(StringComparer.Ordinal);
    private readonly List<Task> myEventTasks = [];
    private readonly List<CancellationTokenSource> mySessionCancellations = [];
    private readonly CancellationTokenSource myEventCancellation = new();
    private readonly object myTeardownLock = new();
    private IAgentRuntime? myRuntime;
    private Task<IReadOnlyList<Exception>>? myTeardown;

    public SessionGeneration(
        IAgentBackend agentBackend,
        Action<AgentEvent> eventSink,
        SquadViewModel viewModel,
        CancellationToken stoppingToken)
    {
        myAgentBackend = agentBackend;
        myEventSink = eventSink;
        myViewModel = viewModel;
        myStoppingToken = stoppingToken;
    }

    public IReadOnlyDictionary<string, IAgentSession> Sessions => mySessions;

    public async Task StartAsync(Func<IAgentSession, Task> onSessionRegistered, CancellationToken cancellationToken = default)
    {
        myRuntime = await myAgentBackend.CreateRuntimeAsync(cancellationToken);
        await myRuntime.StartAsync(session => RegisterSessionAsync(session, onSessionRegistered), cancellationToken);
    }

    private async Task RegisterSessionAsync(IAgentSession session, Func<IAgentSession, Task> onSessionRegistered)
    {
        await onSessionRegistered(session);
        mySessions.Add(session.Role, session);
        var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(myEventCancellation.Token);
        mySessionCancellations.Add(sessionCancellation);
        var eventTask = ObserveEventsAsync(session, sessionCancellation.Token);
        myEventTasks.Add(ObserveSessionAsync(session, sessionCancellation, eventTask));
    }

    // Idempotent, failure-collecting teardown: cancels event observation, asks the runtime owner to resolve
    // session completion and retire its resources, then drains observers and disposes their cancellation sources.
    public Task<IReadOnlyList<Exception>> TeardownAsync()
    {
        lock (myTeardownLock)
            myTeardown ??= TeardownCoreAsync();
        return myTeardown;
    }

    private async Task<IReadOnlyList<Exception>> TeardownCoreAsync()
    {
        var failures = new List<Exception>();
        myEventCancellation.Cancel();
        if (myRuntime is not null)
        {
            try
            {
                await myRuntime.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        try
        {
            await Task.WhenAll(myEventTasks);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        foreach (var sessionCancellation in mySessionCancellations)
            sessionCancellation.Dispose();
        mySessionCancellations.Clear();
        myEventTasks.Clear();
        mySessions.Clear();
        myEventCancellation.Dispose();
        return failures;
    }

    private async Task ObserveEventsAsync(IAgentSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var agentEvent in session.Events(cancellationToken))
            {
                myEventSink(agentEvent);
                await myViewModel.EnqueueEventAsync(session.Role, agentEvent);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException exception) when (exception.Message == "Squad is shutting down")
        {
        }
        catch (Exception eventFailure)
        {
            try
            {
                await session.Completion;
            }
            catch
            {
                return;
            }
            ExceptionDispatchInfo.Capture(eventFailure).Throw();
        }
    }

    private async Task ObserveSessionAsync(
        IAgentSession session,
        CancellationTokenSource sessionCancellation,
        Task eventTask)
    {
        Exception? failure = null;
        try
        {
            await session.Completion;
        }
        catch (OperationCanceledException)
        {
            failure = new OperationCanceledException($"Session '{session.Role}' was canceled.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        sessionCancellation.Cancel();
        try
        {
            await eventTask;
        }
        catch when (failure is not null)
        {
        }

        if (failure is not null && !myStoppingToken.IsCancellationRequested)
        {
            try
            {
                await myViewModel.MarkRoleFailedAsync(session.Role, failure);
            }
            catch (Exception exception) when (
                myStoppingToken.IsCancellationRequested &&
                exception is OperationCanceledException or InvalidOperationException)
            {
            }
        }
    }
}
