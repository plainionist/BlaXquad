using squad.AgentProvider.Abstractions;
using squad.Application;
using System.Runtime.ExceptionServices;

namespace squad.Runtime;

/// <summary>
/// The sole owner of one backend generation's runtime handle, registered-session projection, event/completion
/// observer tasks, and observer cancellation sources. It is created by and bound to one <see cref="Squad"/>
/// generation - it observes and projects only into that generation's <see cref="SquadMembers"/> and dies with it.
/// It never disposes sessions directly; session disposal is entirely the runtime owner's responsibility.
/// </summary>
internal sealed class SessionGeneration
{
    private readonly IAgentBackend myAgentBackend;
    private readonly SquadMembers myMembers;
    private readonly CancellationToken myStoppingToken;
    private readonly List<Task> myEventTasks = [];
    private readonly List<CancellationTokenSource> mySessionCancellations = [];
    private readonly CancellationTokenSource myEventCancellation = new();
    private readonly object myTeardownLock = new();
    private IAgentRuntime? myRuntime;
    private Task<IReadOnlyList<Exception>>? myTeardown;

    public SessionGeneration(
        IAgentBackend agentBackend,
        SquadMembers members,
        CancellationToken stoppingToken)
    {
        myAgentBackend = agentBackend;
        myMembers = members;
        myStoppingToken = stoppingToken;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        myRuntime = await myAgentBackend.CreateRuntimeAsync(cancellationToken);
        await myRuntime.StartAsync(RegisterSessionAsync, cancellationToken);
    }

    private Task RegisterSessionAsync(IAgentSession session)
    {
        myMembers.RegisterSession(session);
        var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(myEventCancellation.Token);
        mySessionCancellations.Add(sessionCancellation);
        var eventTask = ObserveEventsAsync(session, sessionCancellation.Token);
        myEventTasks.Add(ObserveSessionAsync(session, sessionCancellation, eventTask));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels event observation, retires the runtime, and drains observers while collecting failures. A failed
    /// teardown remains retryable and retains resources whose retirement did not complete.
    /// </summary>
    public async Task<IReadOnlyList<Exception>> TeardownAsync()
    {
        Task<IReadOnlyList<Exception>> current;
        lock (myTeardownLock)
            current = myTeardown ??= TeardownCoreAsync();
        var failures = await current;
        if (failures.Count > 0)
        {
            lock (myTeardownLock)
            {
                if (myTeardown == current)
                {
                    myTeardown = null;
                }
            }
        }
        return failures;
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
                myRuntime = null;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (failures.Count == 0)
        {
            try
            {
                await Task.WhenAll(myEventTasks);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (failures.Count > 0)
        {
            return failures;
        }
        foreach (var sessionCancellation in mySessionCancellations)
        {
            sessionCancellation.Dispose();
        }
        mySessionCancellations.Clear();
        myEventTasks.Clear();
        myEventCancellation.Dispose();
        return failures;
    }

    private async Task ObserveEventsAsync(IAgentSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var agentEvent in session.Events(cancellationToken))
            {
                await myMembers.EnqueueEventAsync(session.Role, agentEvent);
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
                await myMembers.MarkRoleFailedAsync(session.Role, failure);
            }
            catch (Exception exception) when (
                myStoppingToken.IsCancellationRequested &&
                exception is OperationCanceledException or InvalidOperationException)
            {
            }
        }
    }
}
