using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Specs.Support;

/// <summary>
/// Controllable <see cref="IAgentProviderFactory"/> loaded into the real, separately launched squad-hq process for
/// the active-usage-refresh Gherkin scenario. Every session connects out to the test-owned
/// <see cref="UsageControlServer"/> named pipe identified by <see cref="PipeNameEnvironmentVariable"/> and stays
/// "still working" (its <see cref="IAgentSession.SendAsync"/> does not complete) until the test commands it to go
/// idle. This proves that provider-reported context and AIC usage reaches the real UI protocol while a role is
/// still working, not only after idle, without depending on any actual coding agent. It has no other configuration
/// surface (that richer fixture design belongs to a later slice).
/// </summary>
public sealed class ControllableAgentProviderFactory : IAgentProviderFactory
{
    public const string PipeNameEnvironmentVariable = "BLAXQUAD_TEST_USAGE_PIPE";

    public string Name => "controllable-fixture";

    public Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IAgentBackend>(new ControllableAgentBackend(context));
}

internal sealed class ControllableAgentBackend(AgentBackendContext context) : IAgentBackend
{
    public Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IAgentRuntime>(new ControllableAgentRuntime(context));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ControllableAgentRuntime(AgentBackendContext context) : IAgentRuntime
{
    private readonly Dictionary<string, ControllableAgentSession> mySessionsByRole = new(StringComparer.Ordinal);
    private UsageControlClient? myControl;
    private Task? myDispatchLoop;

    public async Task StartAsync(Func<IAgentSession, Task> sessionStarted, CancellationToken cancellationToken = default)
    {
        var pipeName = Environment.GetEnvironmentVariable(ControllableAgentProviderFactory.PipeNameEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new InvalidOperationException(
                $"The '{ControllableAgentProviderFactory.PipeNameEnvironmentVariable}' environment variable must name the test control pipe.");
        }

        myControl = await UsageControlClient.ConnectAsync(pipeName, cancellationToken);

        foreach (var role in context.Roles)
        {
            var session = new ControllableAgentSession(role.Role);
            mySessionsByRole.Add(role.Role, session);
            await sessionStarted(session);
            await myControl.NotifyStartedAsync(role.Role, cancellationToken);
        }

        myDispatchLoop = myControl.RunAsync(mySessionsByRole, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in mySessionsByRole.Values)
        {
            await session.DisposeAsync();
        }
        if (myControl is not null)
        {
            await myControl.DisposeAsync();
        }
        if (myDispatchLoop is not null)
        {
            try
            {
                await myDispatchLoop;
            }
            catch
            {
            }
        }
    }
}

internal sealed class ControllableAgentSession : IAgentSession
{
    private readonly AgentEventChannel myEvents = new();
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource mySendCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ControllableAgentSession(string role)
    {
        Role = role;
        myEvents.Publish(new AgentStartedEvent(DateTimeOffset.UtcNow));
    }

    public string Role { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("n");
    public Task Completion => myCompletion.Task;

    public IAsyncEnumerable<AgentEvent> Events(CancellationToken cancellationToken = default) =>
        myEvents.ReadAllAsync(cancellationToken);

    /// <summary>Publishes the user message and then stays outstanding until <see cref="GoIdle"/> is commanded,
    /// keeping the role "still working" so mid-turn usage events can be observed before idle.</summary>
    public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        myEvents.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, prompt));
        return mySendCompletion.Task;
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

    /// <summary>Publishes newer context and AIC usage while the role remains "still working".</summary>
    internal void PublishUsage(long contextUsed, long contextLimit, decimal aicUsed)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        myEvents.Publish(new AgentContextUsageEvent(occurredAt, contextUsed, contextLimit));
        myEvents.Publish(new AgentSessionUsageEvent(occurredAt, aicUsed));
    }

    /// <summary>Publishes final usage, then the idle transition, and completes the outstanding send.</summary>
    internal void GoIdle(long contextUsed, long contextLimit, decimal aicUsed)
    {
        PublishUsage(contextUsed, contextLimit, aicUsed);
        myEvents.Publish(new AgentIdleEvent(DateTimeOffset.UtcNow));
        mySendCompletion.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        myEvents.Complete();
        mySendCompletion.TrySetResult();
        myCompletion.TrySetResult();
        await myEvents.DisposeAsync();
    }
}
