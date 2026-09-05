using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squadHQ.Commands;

namespace squad.Specs.Support;

public sealed class RecordingAgentBackend : IAgentBackend, IAgentBackendFailureSource
{
    private readonly List<RecordingAgentSession> mySessions = [];
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
    public bool RuntimeCreated { get; private set; }
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

    public Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeCreated = true;
        Trace?.Record("backend.runtimeCreated");
        IAgentRuntime runtime = new RecordingAgentRuntime(
            mySessions,
            myEarlyEvents,
            myInitialInstructions,
            FailAfterCreatingSessionCount,
            FailDuringStart,
            BlockBeforeSessionIndex,
            BlockDispose,
            Trace,
            onRegistrationBlocked: () => myRegistrationBlocked.TrySetResult(),
            registrationGate: myRegistrationGate.Task,
            onDisposeEntered: () =>
            {
                Disposed = true;
                myDisposeEntered.TrySetResult();
            },
            disposeGate: myDisposeGate.Task);
        return Task.FromResult(runtime);
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void ReleaseDispose() => myDisposeGate.TrySetResult();
    public void ReleaseRegistration() => myRegistrationGate.TrySetResult();
    public void FailBackend(string message) =>
        myFailure.TrySetException(new InvalidOperationException(message));
}
