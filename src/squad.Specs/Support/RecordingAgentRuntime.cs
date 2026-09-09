using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Specs.Support;

/// <summary>
/// The sole owner of one generation's recorded sessions. Mirrors the real Copilot SDK runtime handle: it starts
/// (and, on partial failure, rolls back) every session it creates, and disposes them all - published or not - as
/// part of its own idempotent teardown.
/// </summary>
public sealed class RecordingAgentRuntime : IAgentRuntime
{
    private readonly List<RecordingAgentSession> mySessions;
    private readonly IReadOnlyDictionary<string, AgentEvent> myEarlyEvents;
    private readonly IReadOnlyDictionary<string, string> myInitialInstructions;
    private readonly bool myFailDuringStart;
    private readonly int myBlockBeforeSessionIndex;
    private readonly Action myOnRegistrationBlocked;
    private readonly Task myRegistrationGate;
    private readonly Action myOnDisposeEntered;
    private readonly HashSet<RecordingAgentSession> myRetiredSessions = [];
    private bool myDisposed;

    internal RecordingAgentRuntime(
        List<RecordingAgentSession> sessions,
        IReadOnlyDictionary<string, AgentEvent> earlyEvents,
        IReadOnlyDictionary<string, string> initialInstructions,
        bool failDuringStart,
        int blockBeforeSessionIndex,
        Action onRegistrationBlocked,
        Task registrationGate,
        Action onDisposeEntered)
    {
        mySessions = sessions;
        myEarlyEvents = earlyEvents;
        myInitialInstructions = initialInstructions;
        myFailDuringStart = failDuringStart;
        myBlockBeforeSessionIndex = blockBeforeSessionIndex;
        myOnRegistrationBlocked = onRegistrationBlocked;
        myRegistrationGate = registrationGate;
        myOnDisposeEntered = onDisposeEntered;
    }

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionStarted);
        for (var index = 0; index < mySessions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = mySessions[index];
            if (index == myBlockBeforeSessionIndex)
            {
                myOnRegistrationBlocked();
                await myRegistrationGate.WaitAsync(cancellationToken);
            }
            if (myEarlyEvents.TryGetValue(session.Role, out var earlyEvent))
            {
                session.Emit(earlyEvent);
            }
            await sessionStarted(session);
            if (myInitialInstructions.TryGetValue(session.Role, out var initialInstruction))
            {
                await session.SendAsync(initialInstruction, cancellationToken);
            }
        }
        if (myFailDuringStart)
        {
            throw new InvalidOperationException("recording backend start failed");
        }
    }

    // Retry-safe: a session is retired only once its own disposal succeeds. A failed attempt collects failures but
    // leaves not-yet-retired sessions in place, so a later call resumes exactly the remaining work instead of
    // treating the failed attempt as terminal.
    public async ValueTask DisposeAsync()
    {
        if (myDisposed)
        {
            return;
        }
        myOnDisposeEntered();
        var failures = new List<Exception>();
        for (var index = mySessions.Count - 1; index >= 0; index--)
        {
            var session = mySessions[index];
            if (myRetiredSessions.Contains(session))
            {
                continue;
            }
            try
            {
                await session.DisposeAsync();
                myRetiredSessions.Add(session);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
        myDisposed = true;
    }
}
