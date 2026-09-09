using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace squad.Specs.Support;

public sealed class RecordingAgentSession : IAgentSession
{
    private readonly AgentEventChannel myEvents;
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int myActiveSends;
    private bool myDisposed;

    public RecordingAgentSession(string role, int capacity = 100, TimeSpan? writeTimeout = null)
    {
        Role = role;
        SessionId = $"recording-{role}";
        myEvents = new AgentEventChannel(capacity, writeTimeout, exception => myCompletion.TrySetException(exception));
    }

    public string Role { get; }
    public string SessionId { get; }
    public Task Completion => myCompletion.Task;
    public ConcurrentQueue<string> Sends { get; } = new();
    public TimeSpan SendDelay { get; set; }
    public bool OverlappedSend { get; private set; }
    public bool Disposed => myDisposed;
    public int DisposeCount { get; private set; }
    public Action? OnDisposeObserved { get; set; }
    public Action<string>? OnSend { get; set; }

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (myDisposed)
        {
            throw new ObjectDisposedException(nameof(RecordingAgentSession));
        }
        if (Interlocked.Increment(ref myActiveSends) > 1)
        {
            OverlappedSend = true;
        }
        try
        {
            Sends.Enqueue(prompt);
            OnSend?.Invoke(prompt);
            if (SendDelay > TimeSpan.Zero)
            {
                await Task.Delay(SendDelay, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref myActiveSends);
        }
    }

    public async Task SendHarnessAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Emit(new AgentHarnessMessageEvent(DateTimeOffset.UtcNow, prompt));
        await SendAsync(prompt, cancellationToken);
    }

    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task RespondToPermissionAsync(string requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task RespondToInputAsync(string requestId, AgentInputResponse response, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task RespondToElicitationAsync(string requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var agentEvent in myEvents.ReadAllAsync(cancellationToken))
        {
            yield return agentEvent;
        }
    }

    public void Emit(AgentEvent agentEvent) => myEvents.Publish(agentEvent);

    public async ValueTask DisposeAsync()
    {
        DisposeCount++;
        OnDisposeObserved?.Invoke();
        myDisposed = true;
        myCompletion.TrySetResult();
        await myEvents.DisposeAsync();
    }
}



