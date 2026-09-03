using global::squad.Abstractions;
using global::squad.Abstractions.Agents;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace squad.Specs.Support;

public sealed class RecordingAgentSession : IAgentSession
{
    private readonly AgentEventChannel myEvents;
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myAbortEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myAbortGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    public ConcurrentQueue<(string RequestId, AgentPermissionResponse Response)> PermissionResponses { get; } = new();
    public ConcurrentQueue<(string RequestId, AgentInputResponse Response)> InputResponses { get; } = new();
    public ConcurrentQueue<(string RequestId, AgentElicitationResponse Response)> ElicitationResponses { get; } = new();
    public int AbortCount { get; private set; }
    public int PendingInteractionCancellationCount { get; private set; }
    public TimeSpan SendDelay { get; set; }
    public bool BlockAbort { get; set; }
    public bool FailAbort { get; set; }
    public Task AbortEntered => myAbortEntered.Task;
    public bool OverlappedSend { get; private set; }
    public bool Disposed => myDisposed;
    public bool FailOnDispose { get; set; }
    public bool IgnoreEventCancellation { get; set; }
    public bool LeaveEventsOpenOnDispose { get; set; }
    public bool EventCancellationObserved { get; private set; }
    public bool EventStreamLeftOpen { get; private set; }
    public int DisposeCount { get; private set; }
    public int ActiveSendCountAtDispose { get; private set; }
    public ConcurrentQueue<string> SendOrder { get; } = new();
    public Action? OnDispose { get; set; }
    public Action? OnDisposeObserved { get; set; }
    public Action<string>? OnSend { get; set; }

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (myDisposed)
            throw new ObjectDisposedException(nameof(RecordingAgentSession));
        if (Interlocked.Increment(ref myActiveSends) > 1)
            OverlappedSend = true;
        try
        {
            Sends.Enqueue(prompt);
            SendOrder.Enqueue(prompt);
            OnSend?.Invoke(prompt);
            if (SendDelay > TimeSpan.Zero)
                await Task.Delay(SendDelay, cancellationToken);
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

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!myDisposed)
        {
            AbortCount++;
            myAbortEntered.TrySetResult();
            if (BlockAbort)
                await myAbortGate.Task.WaitAsync(cancellationToken);
            if (FailAbort)
                throw new InvalidOperationException("recording abort failed");
        }
    }

    public void ReleaseAbort() => myAbortGate.TrySetResult();

    public Task RespondToPermissionAsync(string requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PermissionResponses.Enqueue((requestId, response));
        return Task.CompletedTask;
    }

    public Task RespondToInputAsync(string requestId, AgentInputResponse response, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InputResponses.Enqueue((requestId, response));
        return Task.CompletedTask;
    }

    public Task RespondToElicitationAsync(string requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ElicitationResponses.Enqueue((requestId, response));
        return Task.CompletedTask;
    }

    public Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PendingInteractionCancellationCount++;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() => EventCancellationObserved = true);
        var readerCancellation = IgnoreEventCancellation ? CancellationToken.None : cancellationToken;
        await foreach (var agentEvent in myEvents.ReadAllAsync(readerCancellation))
            yield return agentEvent;
    }

    public void Emit(AgentEvent agentEvent) => myEvents.Publish(agentEvent);

    public void Fail(string message) => myCompletion.TrySetException(new InvalidOperationException(message));

    public async ValueTask DisposeAsync()
    {
        DisposeCount++;
        ActiveSendCountAtDispose = Volatile.Read(ref myActiveSends);
        OnDispose?.Invoke();
        OnDisposeObserved?.Invoke();
        myDisposed = true;
        myCompletion.TrySetResult();
        if (LeaveEventsOpenOnDispose)
            EventStreamLeftOpen = true;
        else
            await myEvents.DisposeAsync();
        if (FailOnDispose)
            throw new InvalidOperationException("recording session disposal failed");
    }
}



