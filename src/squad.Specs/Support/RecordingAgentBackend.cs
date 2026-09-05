using global::squad.AgentProvider.Abstractions;
using global::squad.AgentProvider.Abstractions.Agents;
using global::squadHQ.Commands;

namespace squad.Specs.Support;

public sealed class RecordingAgentBackend : IAgentBackend, IAgentBackendFailureSource
{
    private readonly List<RecordingAgentSession> mySessions = [];
    private readonly HashSet<RecordingAgentSession> myPublishedSessions = [];
    private readonly Dictionary<string, AgentEvent> myEarlyEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> myInitialInstructions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> myRoleWorktrees = new(StringComparer.Ordinal);
    private readonly List<string> myDisposeOrder = [];
    private readonly TaskCompletionSource myDisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myDisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myRegistrationBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myRegistrationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<RecordingAgentSession> Sessions => mySessions;
    public bool FailDuringStart { get; set; }
    public int FailAfterCreatingSessionCount { get; set; }
    public bool Disposed { get; private set; }
    public bool BlockDispose { get; set; }
    public Task DisposeEntered => myDisposeEntered.Task;
    public IReadOnlyDictionary<string, string> RoleWorktrees => myRoleWorktrees;
    public IReadOnlyList<string> DisposeOrder => myDisposeOrder;
    public int BlockBeforeSessionIndex { get; set; } = -1;
    public Task RegistrationBlocked => myRegistrationBlocked.Task;
    public Task Failure => myFailure.Task;
    public LifecycleTrace? Trace { get; set; }

    public Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionStarted);
        for (var index = 0; index < mySessions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = mySessions[index];
            if (index == BlockBeforeSessionIndex)
            {
                myRegistrationBlocked.TrySetResult();
                await myRegistrationGate.Task.WaitAsync(cancellationToken);
            }
            if (myEarlyEvents.TryGetValue(session.Role, out var earlyEvent))
                session.Emit(earlyEvent);
            await sessionStarted(session);
            Trace?.Record($"backend.sessionRegistered:{session.Role}");
            myPublishedSessions.Add(session);
            if (myInitialInstructions.TryGetValue(session.Role, out var initialInstruction))
                await session.SendAsync(initialInstruction, cancellationToken);
            if (FailAfterCreatingSessionCount > 0 && index + 1 >= FailAfterCreatingSessionCount)
                throw new InvalidOperationException("recording backend failed after creating sessions");
        }
        if (FailDuringStart)
            throw new InvalidOperationException("recording backend start failed");
    }

    public void AddRole(string role)
    {
        var session = new RecordingAgentSession(role)
        {
            OnDisposeObserved = () => myDisposeOrder.Add(role),
        };
        mySessions.Add(session);
    }

    public void AddSdkRole(RoleConfigRow role, AgentEvent earlyEvent, string initialInstruction)
    {
        AddRole(role.Role);
        myRoleWorktrees.Add(role.Role, role.WorktreePath);
        myEarlyEvents.Add(role.Role, earlyEvent);
        myInitialInstructions.Add(role.Role, initialInstruction);
    }

    public void RemoveRole(string role) => mySessions.RemoveAll(session => session.Role == role);

    public async ValueTask DisposeAsync()
    {
        Disposed = true;
        myDisposeEntered.TrySetResult();
        if (BlockDispose)
            await myDisposeGate.Task;
        var failures = new List<Exception>();
        foreach (var session in mySessions.Where(session => !myPublishedSessions.Contains(session)))
        {
            try
            {
                session.DisposeAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        Trace?.Record("backend.disposed");
        if (failures.Count > 0)
            throw new AggregateException(failures);
    }

    public void ReleaseDispose() => myDisposeGate.TrySetResult();
    public void ReleaseRegistration() => myRegistrationGate.TrySetResult();
    public void FailBackend(string message) =>
        myFailure.TrySetException(new InvalidOperationException(message));
}



