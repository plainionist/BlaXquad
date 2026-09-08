using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Specs.Support;

/// <summary>
/// Provider-side session for <see cref="FakeAgentProviderFactory"/>. Establishes itself by publishing the real
/// "started" provider event through the production event channel, and otherwise implements only the minimal
/// production <see cref="IAgentSession"/> lifecycle this slice needs to prove - no prompt handling, permission, or
/// elicitation behavior (that richer control surface belongs to a later slice).
/// </summary>
internal sealed class FakeAgentSession : IAgentSession
{
    private readonly AgentEventChannel myEvents = new();
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeAgentSession(string role)
    {
        Role = role;
        myEvents.Publish(new AgentStartedEvent(DateTimeOffset.UtcNow));
    }

    public string Role { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("n");
    public Task Completion => myCompletion.Task;

    public IAsyncEnumerable<AgentEvent> Events(CancellationToken cancellationToken = default) =>
        myEvents.ReadAllAsync(cancellationToken);

    public Task SendAsync(string prompt, CancellationToken cancellationToken = default) => Task.CompletedTask;

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
