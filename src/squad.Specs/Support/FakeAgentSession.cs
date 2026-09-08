using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Specs.Support;

/// <summary>
/// Provider-side session for <see cref="FakeAgentProviderFactory"/>. Establishes itself by publishing the real
/// "started" provider event through the production event channel. Without a control transport, <see cref="SendAsync"/>
/// mirrors the minimal Slice 6 behavior (publish the user message, then go idle) with no prompt handling,
/// permission, or elicitation behavior. With a control transport, it instead reports the prompt across the pipe
/// and awaits a semantic reply delivered through <see cref="DeliverReply"/>, publishing it as the real production
/// <see cref="AgentAssistantMessageEvent"/> before going idle - this is what lets a black-box scenario drive an
/// assistant reply through the real transcript without any product test hook.
/// </summary>
internal sealed class FakeAgentSession : IAgentSession
{
    private readonly AgentEventChannel myEvents = new();
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FakeProviderControlClient? myControl;
    private TaskCompletionSource<string>? myPendingReply;

    public FakeAgentSession(string role, FakeProviderControlClient? control = null)
    {
        Role = role;
        myControl = control;
        myEvents.Publish(new AgentStartedEvent(DateTimeOffset.UtcNow));
    }

    public string Role { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("n");
    public bool IsDisposed { get; private set; }
    public Task Completion => myCompletion.Task;

    public IAsyncEnumerable<AgentEvent> Events(CancellationToken cancellationToken = default) =>
        myEvents.ReadAllAsync(cancellationToken);

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        myEvents.Publish(new AgentUserMessageEvent(now, prompt));

        if (myControl is null)
        {
            myEvents.Publish(new AgentIdleEvent(DateTimeOffset.UtcNow));
            return;
        }

        var pendingReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        myPendingReply = pendingReply;
        await myControl.NotifyPromptAsync(Role, SessionId, prompt, cancellationToken);
        var content = await pendingReply.Task;
        myEvents.Publish(new AgentAssistantMessageEvent(DateTimeOffset.UtcNow, content, IsDelta: false));
        myEvents.Publish(new AgentIdleEvent(DateTimeOffset.UtcNow));
    }

    /// <summary>Delivers a semantic reply received across the control pipe to whichever <see cref="SendAsync"/>
    /// call is currently waiting for it.</summary>
    public void DeliverReply(string content) => myPendingReply?.TrySetResult(content);

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
        IsDisposed = true;
        myPendingReply?.TrySetCanceled();
        myEvents.Complete();
        myCompletion.TrySetResult();
        await myEvents.DisposeAsync();
    }
}

