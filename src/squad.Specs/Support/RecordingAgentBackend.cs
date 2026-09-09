using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Workspaces;

namespace squad.Specs.Support;

public sealed class RecordingAgentBackend : IAgentBackend
{
    private readonly List<RecordingAgentSession> mySessions = [];
    private readonly Dictionary<string, AgentEvent> myEarlyEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> myInitialInstructions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> myRoleWorktrees = new(StringComparer.Ordinal);
    private readonly List<string> myDisposeOrder = [];
    private readonly TaskCompletionSource myRegistrationBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myRegistrationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<RecordingAgentSession> Sessions => mySessions;
    public bool RuntimeCreated { get; private set; }
    public bool FailDuringStart { get; set; }
    public bool Disposed { get; private set; }
    public IReadOnlyDictionary<string, string> RoleWorktrees => myRoleWorktrees;
    public IReadOnlyList<string> DisposeOrder => myDisposeOrder;
    public int BlockBeforeSessionIndex { get; set; } = -1;
    public Task RegistrationBlocked => myRegistrationBlocked.Task;

    public Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeCreated = true;
        IAgentRuntime runtime = new RecordingAgentRuntime(
            mySessions,
            myEarlyEvents,
            myInitialInstructions,
            FailDuringStart,
            BlockBeforeSessionIndex,
            onRegistrationBlocked: () => myRegistrationBlocked.TrySetResult(),
            registrationGate: myRegistrationGate.Task,
            onDisposeEntered: () => Disposed = true);
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void ReleaseRegistration() => myRegistrationGate.TrySetResult();
}
