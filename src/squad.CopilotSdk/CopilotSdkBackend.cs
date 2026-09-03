using global::squad.Abstractions;
using global::squad.Abstractions.Agents;

namespace squad.CopilotSdk;

public sealed class CopilotSdkBackend : IAgentBackend, IAgentBackendFailureSource
{
    private static readonly TimeSpan myGracefulStopTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<AgentBackendContext> myContext;
    private readonly List<CopilotSdkAgentSession> mySessions = [];
    private readonly object myLifecycleLock = new();
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CopilotSdkClient? myClient;
    private Task? myForceStop;
    private bool myDisposed;

    public CopilotSdkBackend(Func<AgentBackendContext> context)
    {
        myContext = context;
    }

    public Task Failure => myFailure.Task;

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (myClient is not null)
            return;
        var context = myContext();
        myClient = await CopilotSdkClient.StartAsync(context.WorkingDirectory, context.Environment, cancellationToken);
    }

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionStarted);
        await PrepareAsync(cancellationToken);
        try
        {
            var createTasks = myContext().Roles.Select(async role =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var session = new CopilotSdkAgentSession(
                    role.Role,
                    escalateTeardownFailure: RequestForceStop);
                var runtimeSession = await myClient!.CreateSessionAsync(role.WorktreePath, session, role.Permissions, role.Model, role.Effort, cancellationToken);
                session.Attach(runtimeSession);
                session.Publish(new AgentSessionConfigurationEvent(DateTimeOffset.UtcNow, role.Model, role.Effort));
                lock (mySessions)
                {
                    mySessions.Add(session);
                }
                return (role, session);
            }).ToArray();

            var sessions = await Task.WhenAll(createTasks);

            foreach (var (role, session) in sessions)
            {
                await sessionStarted(session);
            }

            var harnessTasks = sessions.Select(pair =>
                pair.session.SendHarnessAsync(pair.role.InitialInstruction, cancellationToken)
            ).ToArray();

            await Task.WhenAll(harnessTasks);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (myDisposed)
            return;
        myDisposed = true;

        var failures = new List<Exception>();
        for (var index = mySessions.Count - 1; index >= 0; index--)
        {
            try
            {
                await mySessions[index].DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        mySessions.Clear();

        if (myClient is not null)
        {
            Task? forceStop;
            lock (myLifecycleLock)
                forceStop = myForceStop;
            if (forceStop is not null)
            {
                try
                {
                    await forceStop;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            else
            {
                try
                {
                    await myClient.StopAsync().WaitAsync(myGracefulStopTimeout);
                }
                catch (TimeoutException)
                {
                    try
                    {
                        await myClient.ForceStopAsync().WaitAsync(myGracefulStopTimeout);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            try
            {
                await myClient.DisposeAsync().AsTask().WaitAsync(myGracefulStopTimeout);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            myClient = null;
        }

        if (failures.Count > 0)
            throw new AggregateException(failures);
    }

    private void RequestForceStop(Exception sessionFailure)
    {
        lock (myLifecycleLock)
            myForceStop ??= ForceStopAsync(sessionFailure);
    }

    private async Task ForceStopAsync(Exception sessionFailure)
    {
        var backendFailure = new AggregateException(
            "The shared Copilot SDK runtime must be force-stopped after a session teardown failure.",
            sessionFailure);
        CopilotSdkAgentSession[] sessions;
        lock (mySessions)
            sessions = mySessions.ToArray();
        foreach (var session in sessions)
            session.FailFromBackend(backendFailure);

        var client = myClient;
        try
        {
            if (client is not null)
                await client.ForceStopAsync().WaitAsync(myGracefulStopTimeout);
        }
        catch (Exception forceStopFailure)
        {
            var fatalFailure = new AggregateException(
                "The shared Copilot SDK runtime could not be force-stopped.",
                backendFailure,
                forceStopFailure);
            myFailure.TrySetException(fatalFailure);
            throw fatalFailure;
        }
    }
}



