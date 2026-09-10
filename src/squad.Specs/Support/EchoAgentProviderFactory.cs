using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Specs.Support;

/// <summary>
/// Minimal, hardcoded <see cref="IAgentProviderFactory"/> loaded into the real, separately launched squad-hq
/// process for the stdio UI protocol black-box scenarios. It has no configuration surface and no control pipe
/// (that richer fixture design belongs to a later slice): every session simply echoes back each prompt it
/// receives as a completed assistant message, which is enough to drive a real transcript update through the real
/// protocol pipeline without depending on any actual coding agent.
/// </summary>
public sealed class EchoAgentProviderFactory : IAgentProviderFactory
{
    public string Name => "echo-fixture";

    public Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IAgentBackend>(new EchoAgentBackend(context));
}

internal sealed class EchoAgentBackend(AgentBackendContext context) : IAgentBackend
{
    public Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IAgentRuntime>(new EchoAgentRuntime(context));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class EchoAgentRuntime(AgentBackendContext context) : IAgentRuntime
{
    private readonly List<EchoAgentSession> mySessions = [];

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        foreach (var role in context.Roles)
        {
            var session = new EchoAgentSession(role.Role);
            mySessions.Add(session);
            await sessionStarted(session);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in mySessions)
        {
            await session.DisposeAsync();
        }
    }
}

internal sealed class EchoAgentSession : IAgentSession
{
    private readonly TestAgentEventStream myEvents = new();
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public EchoAgentSession(string role)
    {
        Role = role;
        myEvents.Publish(new AgentStartedEvent(DateTimeOffset.UtcNow));
    }

    public string Role { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("n");
    public Task Completion => myCompletion.Task;

    public IAsyncEnumerable<AgentEvent> Events(CancellationToken cancellationToken = default) =>
        myEvents.ReadAllAsync(cancellationToken);

    public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        myEvents.Publish(new AgentUserMessageEvent(now, prompt));
        myEvents.Publish(new AgentAssistantMessageEvent(now, $"echo: {prompt}", IsDelta: false));
        myEvents.Publish(new AgentIdleEvent(now));
        return Task.CompletedTask;
    }

    public Task SendHarnessAsync(string prompt, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RespondToPermissionAsync(string requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RespondToInputAsync(string requestId, AgentInputResponse response, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RespondToElicitationAsync(string requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        myEvents.Complete();
        myCompletion.TrySetResult();
        await myEvents.DisposeAsync();
    }
}
