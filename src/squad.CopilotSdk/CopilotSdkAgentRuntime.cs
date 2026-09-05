using global::squad.AgentProvider.Abstractions;
using global::squad.AgentProvider.Abstractions.Agents;

namespace squad.CopilotSdk;

/// <summary>
/// The sole owner of one generation's Copilot SDK client connection and every session it creates. Nothing outside
/// this runtime disposes those sessions or the client, including after a partial startup failure.
/// </summary>
internal sealed class CopilotSdkAgentRuntime : IAgentRuntime
{
    private static readonly TimeSpan myGracefulStopTimeout = TimeSpan.FromSeconds(10);
    private readonly CopilotSdkClient myClient;
    private readonly AgentBackendContext myContext;
    private readonly Action<Exception> myReportFatalFailure;
    private readonly List<CopilotSdkAgentSession> mySessions = [];
    private readonly object myLifecycleLock = new();
    private Task? myForceStop;
    private bool myDisposed;

    public CopilotSdkAgentRuntime(CopilotSdkClient client, AgentBackendContext context, Action<Exception> reportFatalFailure)
    {
        myClient = client;
        myContext = context;
        myReportFatalFailure = reportFatalFailure;
    }

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionStarted);
        try
        {
            var createTasks = myContext.Roles.Select(async role =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var session = new CopilotSdkAgentSession(
                    role.Role,
                    escalateTeardownFailure: RequestForceStop);
                var runtimeSession = await myClient.CreateSessionAsync(role.WorktreePath, session, role.Permissions, role.Model, role.Effort, cancellationToken);
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
        CopilotSdkAgentSession[] sessions;
        lock (mySessions)
            sessions = [.. mySessions];
        for (var index = sessions.Length - 1; index >= 0; index--)
        {
            try
            {
                await sessions[index].DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        lock (mySessions)
            mySessions.Clear();

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
        var runtimeFailure = new AggregateException(
            "The shared Copilot SDK runtime must be force-stopped after a session teardown failure.",
            sessionFailure);
        CopilotSdkAgentSession[] sessions;
        lock (mySessions)
            sessions = [.. mySessions];
        foreach (var session in sessions)
            session.FailFromBackend(runtimeFailure);

        try
        {
            await myClient.ForceStopAsync().WaitAsync(myGracefulStopTimeout);
        }
        catch (Exception forceStopFailure)
        {
            var fatalFailure = new AggregateException(
                "The shared Copilot SDK runtime could not be force-stopped.",
                runtimeFailure,
                forceStopFailure);
            myReportFatalFailure(fatalFailure);
            throw fatalFailure;
        }
    }
}
