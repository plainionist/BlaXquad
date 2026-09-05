using global::squad.AgentProvider.Abstractions;
using global::squad.AgentProvider.Abstractions.Agents;

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
    private readonly int myFailAfterCreatingSessionCount;
    private readonly bool myFailDuringStart;
    private readonly int myBlockBeforeSessionIndex;
    private readonly bool myBlockDispose;
    private readonly LifecycleTrace? myTrace;
    private readonly Action myOnRegistrationBlocked;
    private readonly Task myRegistrationGate;
    private readonly Action myOnDisposeEntered;
    private readonly Task myDisposeGate;
    private bool myDisposed;

    internal RecordingAgentRuntime(
        List<RecordingAgentSession> sessions,
        IReadOnlyDictionary<string, AgentEvent> earlyEvents,
        IReadOnlyDictionary<string, string> initialInstructions,
        int failAfterCreatingSessionCount,
        bool failDuringStart,
        int blockBeforeSessionIndex,
        bool blockDispose,
        LifecycleTrace? trace,
        Action onRegistrationBlocked,
        Task registrationGate,
        Action onDisposeEntered,
        Task disposeGate)
    {
        mySessions = sessions;
        myEarlyEvents = earlyEvents;
        myInitialInstructions = initialInstructions;
        myFailAfterCreatingSessionCount = failAfterCreatingSessionCount;
        myFailDuringStart = failDuringStart;
        myBlockBeforeSessionIndex = blockBeforeSessionIndex;
        myBlockDispose = blockDispose;
        myTrace = trace;
        myOnRegistrationBlocked = onRegistrationBlocked;
        myRegistrationGate = registrationGate;
        myOnDisposeEntered = onDisposeEntered;
        myDisposeGate = disposeGate;
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
                session.Emit(earlyEvent);
            await sessionStarted(session);
            myTrace?.Record($"backend.sessionRegistered:{session.Role}");
            if (myInitialInstructions.TryGetValue(session.Role, out var initialInstruction))
                await session.SendAsync(initialInstruction, cancellationToken);
            if (myFailAfterCreatingSessionCount > 0 && index + 1 >= myFailAfterCreatingSessionCount)
                throw new InvalidOperationException("recording backend failed after creating sessions");
        }
        if (myFailDuringStart)
            throw new InvalidOperationException("recording backend start failed");
    }

    public async ValueTask DisposeAsync()
    {
        if (myDisposed)
            return;
        myDisposed = true;
        myOnDisposeEntered();
        if (myBlockDispose)
            await myDisposeGate;
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
        myTrace?.Record("backend.disposed");
        if (failures.Count > 0)
            throw new AggregateException(failures);
    }
}
