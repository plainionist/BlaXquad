using global::squad.AgentProvider.Abstractions;
using global::squad.AgentProvider.Abstractions.Agents;
using System.Text.Json;

namespace squad.CopilotSdk;

internal sealed class CopilotSdkAgentSession : IAgentSession
{
    private static readonly TimeSpan myDefaultFailureTeardownTimeout = TimeSpan.FromSeconds(5);
    private readonly AgentEventChannel myEvents;
    private readonly TimeSpan myFailureTeardownTimeout;
    private readonly Action<Exception>? myEscalateTeardownFailure;
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object myInteractionLock = new();
    private readonly Dictionary<string, TaskCompletionSource<AgentPermissionResponse>> myPendingPermissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<AgentInputResponse>> myPendingInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<AgentElicitationResponse>> myPendingElicitations = new(StringComparer.Ordinal);
    private readonly Queue<string> myPendingHarnessMessageEchoes = new();
    private readonly TaskCompletionSource myFailureTeardown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CopilotSdkRuntimeSession? myRuntimeSession;
    private Exception? myFailure;
    private int myContextRefreshInFlight;
    private int myUsageRefreshInFlight;
    private bool myDisposed;

    public CopilotSdkAgentSession(
        string role,
        int capacity = 100,
        TimeSpan? writeTimeout = null,
        TimeSpan? failureTeardownTimeout = null,
        Action<Exception>? escalateTeardownFailure = null)
    {
        Role = role;
        myFailureTeardownTimeout = failureTeardownTimeout ?? myDefaultFailureTeardownTimeout;
        myEscalateTeardownFailure = escalateTeardownFailure;
        myEvents = new AgentEventChannel(capacity, writeTimeout, FailSession);
    }

    public string Role { get; }
    public string SessionId => myRuntimeSession?.SessionId ?? throw new InvalidOperationException("Copilot session has not been created");
    public Task Completion => myCompletion.Task;

    internal void Attach(CopilotSdkRuntimeSession runtimeSession)
    {
        myRuntimeSession = runtimeSession;
        runtimeSession.StartContextWindowResolution();
        RefreshContextUsage();
        RefreshUsage();
    }

    public void Publish(AgentEvent agentEvent) => myEvents.Publish(agentEvent);

    internal Task PublishAsync(AgentEvent agentEvent, CancellationToken cancellationToken = default) =>
        myEvents.PublishAsync(agentEvent, cancellationToken);

    public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        return RequireRuntimeSession().SendAsync(prompt, cancellationToken);
    }

    public Task SendHarnessAsync(string prompt, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        lock (myInteractionLock)
            myPendingHarnessMessageEchoes.Enqueue(prompt);
        Publish(new AgentHarnessMessageEvent(DateTimeOffset.UtcNow, prompt));
        return SendAsync(prompt, cancellationToken);
    }

    internal bool TryConsumeHarnessMessageEcho(string content)
    {
        lock (myInteractionLock)
        {
            if (myPendingHarnessMessageEchoes.Count == 0 || !string.Equals(myPendingHarnessMessageEchoes.Peek(), content, StringComparison.Ordinal))
                return false;
            myPendingHarnessMessageEchoes.Dequeue();
            return true;
        }
    }

    internal void RefreshContextUsage()
    {
        if (myDisposed || Interlocked.Exchange(ref myContextRefreshInFlight, 1) != 0)
            return;
        _ = RefreshContextUsageAsync();
    }

    internal void RefreshUsage()
    {
        if (myDisposed || Interlocked.Exchange(ref myUsageRefreshInFlight, 1) != 0)
            return;
        _ = RefreshUsageAsync();
    }

    public Task AbortAsync(CancellationToken cancellationToken = default) =>
        AbortCoreAsync(cancellationToken);

    public Task RespondToPermissionAsync(string requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default) =>
        CompleteInteractionAsync(requestId, response, myPendingPermissions, cancellationToken);

    public Task RespondToInputAsync(string requestId, AgentInputResponse response, CancellationToken cancellationToken = default) =>
        CompleteInteractionAsync(requestId, response, myPendingInputs, cancellationToken);

    public Task RespondToElicitationAsync(string requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default) =>
        CompleteInteractionAsync(requestId, response, myPendingElicitations, cancellationToken);

    public Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        CancelPendingInteractionsCore(cancellationToken);
        return Task.CompletedTask;
    }

    private void CancelPendingInteractionsCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelInteractions(myPendingPermissions, cancellationToken);
        CancelInteractions(myPendingInputs, cancellationToken);
        CancelInteractions(myPendingElicitations, cancellationToken);
    }

    internal Task<AgentPermissionResponse> RequestPermissionAsync(string description, CancellationToken cancellationToken = default) =>
        RequestInteractionAsync(new AgentPermissionRequest(DateTimeOffset.UtcNow, CreateInteractionId(), Role, description), myPendingPermissions, cancellationToken);

    internal Task<AgentInputResponse> RequestInputAsync(string prompt, IReadOnlyList<string>? choices, bool allowFreeform, CancellationToken cancellationToken = default) =>
        RequestInteractionAsync(new AgentInputRequest(DateTimeOffset.UtcNow, CreateInteractionId(), Role, prompt, choices, allowFreeform), myPendingInputs, cancellationToken);

    internal Task<AgentElicitationResponse> RequestElicitationAsync(string prompt, string mode, JsonElement? requestedSchema, string? url, CancellationToken cancellationToken = default) =>
        RequestInteractionAsync(new AgentElicitationRequest(DateTimeOffset.UtcNow, CreateInteractionId(), Role, prompt, mode, requestedSchema, url), myPendingElicitations, cancellationToken);

    public async IAsyncEnumerable<AgentEvent> Events([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var agentEvent in myEvents.ReadAllAsync(cancellationToken))
            yield return agentEvent;
    }

    public async ValueTask DisposeAsync()
    {
        if (myDisposed)
            return;
        lock (myInteractionLock)
        {
            if (myDisposed)
                return;
            myDisposed = true;
        }
        try
        {
            CancelPendingInteractionsCore(CancellationToken.None);
            if (myFailure is not null)
                await myFailureTeardown.Task;
            if (myRuntimeSession is not null)
            {
                var disposal = myRuntimeSession.DisposeAsync().AsTask();
                if (myFailure is not null)
                    await disposal.WaitAsync(myFailureTeardownTimeout);
                else
                    await disposal;
            }
        }
        finally
        {
            await myEvents.DisposeAsync();
            myCompletion.TrySetResult();
        }
    }

    private CopilotSdkRuntimeSession RequireRuntimeSession() =>
        myRuntimeSession ?? throw new InvalidOperationException("Copilot session has not been created");

    private async Task RefreshContextUsageAsync()
    {
        try
        {
            var runtimeSession = myRuntimeSession;
            if (runtimeSession is null || myDisposed)
                return;
            var usage = await runtimeSession.GetContextUsageAsync();
            if (usage is { } contextUsage && contextUsage.LimitTokens > 0 && !myDisposed)
                Publish(new AgentContextUsageEvent(DateTimeOffset.UtcNow, contextUsage.UsedTokens, contextUsage.LimitTokens));
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref myContextRefreshInFlight, 0);
        }
    }

    private async Task RefreshUsageAsync()
    {
        try
        {
            var runtimeSession = myRuntimeSession;
            if (runtimeSession is not null && !myDisposed)
                Publish(new AgentSessionUsageEvent(DateTimeOffset.UtcNow, await runtimeSession.GetAicUsageAsync()));
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref myUsageRefreshInFlight, 0);
        }
    }

    private async Task AbortCoreAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        CancelPendingInteractionsCore(cancellationToken);
        await RequireRuntimeSession().AbortAsync(cancellationToken);
    }

    private void FailSession(Exception exception) =>
        TransitionToFailure(exception, teardownRuntimeSession: true);

    internal void FailFromBackend(Exception exception) =>
        TransitionToFailure(exception, teardownRuntimeSession: false);

    private void TransitionToFailure(Exception exception, bool teardownRuntimeSession)
    {
        TaskCompletionSource<AgentPermissionResponse>[] permissions;
        TaskCompletionSource<AgentInputResponse>[] inputs;
        TaskCompletionSource<AgentElicitationResponse>[] elicitations;
        lock (myInteractionLock)
        {
            if (myDisposed || myFailure is not null)
                return;
            myFailure = exception;
            permissions = myPendingPermissions.Values.ToArray();
            inputs = myPendingInputs.Values.ToArray();
            elicitations = myPendingElicitations.Values.ToArray();
            myPendingPermissions.Clear();
            myPendingInputs.Clear();
            myPendingElicitations.Clear();
        }

        myEvents.Complete(exception);
        if (teardownRuntimeSession)
            _ = TeardownFailedRuntimeSessionAsync();
        else
            myFailureTeardown.TrySetResult();
        myCompletion.TrySetException(exception);
        CompleteWithException(permissions, exception);
        CompleteWithException(inputs, exception);
        CompleteWithException(elicitations, exception);
    }

    private async Task TeardownFailedRuntimeSessionAsync()
    {
        if (myRuntimeSession is null)
        {
            myFailureTeardown.TrySetResult();
            return;
        }

        Exception abortFailure;
        try
        {
            using var abortCancellation = new CancellationTokenSource(myFailureTeardownTimeout);
            await myRuntimeSession.AbortAsync(abortCancellation.Token)
                .WaitAsync(myFailureTeardownTimeout)
                .ConfigureAwait(false);
            myFailureTeardown.TrySetResult();
            return;
        }
        catch (Exception exception)
        {
            abortFailure = exception;
        }

        try
        {
            await myRuntimeSession.DisposeAsync()
                .AsTask()
                .WaitAsync(myFailureTeardownTimeout)
                .ConfigureAwait(false);
            myFailureTeardown.TrySetException(abortFailure);
        }
        catch (Exception disposeFailure)
        {
            var teardownFailure = new AggregateException(
                $"Failed to stop overloaded Copilot SDK role '{Role}' within the teardown grace period.",
                abortFailure,
                disposeFailure);
            myEscalateTeardownFailure?.Invoke(teardownFailure);
            myFailureTeardown.TrySetException(teardownFailure);
        }
    }

    private void EnsureActive()
    {
        lock (myInteractionLock)
        {
            if (myFailure is { } failure)
                throw failure;
            if (myDisposed)
                throw new ObjectDisposedException(nameof(CopilotSdkAgentSession));
        }
    }

    private async Task<TResponse> RequestInteractionAsync<TResponse>(AgentEvent request, Dictionary<string, TaskCompletionSource<TResponse>> pendingInteractions, CancellationToken cancellationToken)
    {
        var requestId = GetRequestId(request);
        var completion = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (myInteractionLock)
        {
            if (myFailure is { } failure)
                throw failure;
            if (myDisposed)
                throw new ObjectDisposedException(nameof(CopilotSdkAgentSession));
            pendingInteractions.Add(requestId, completion);
        }
        await PublishAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (myInteractionLock)
                pendingInteractions.Remove(requestId);
        }
    }

    private Task CompleteInteractionAsync<TResponse>(string requestId, TResponse response, Dictionary<string, TaskCompletionSource<TResponse>> pendingInteractions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource<TResponse> completion;
        lock (myInteractionLock)
        {
            if (myFailure is { } failure)
                throw failure;
            if (!pendingInteractions.Remove(requestId, out completion!))
                throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{Role}'.");
        }
        completion.TrySetResult(response);
        return Task.CompletedTask;
    }

    private void CancelInteractions<TResponse>(Dictionary<string, TaskCompletionSource<TResponse>> pendingInteractions, CancellationToken cancellationToken)
    {
        TaskCompletionSource<TResponse>[] completions;
        lock (myInteractionLock)
        {
            completions = pendingInteractions.Values.ToArray();
            pendingInteractions.Clear();
        }
        foreach (var completion in completions)
            completion.TrySetCanceled(cancellationToken);
    }

    private void CancelInteractions<TResponse>(Dictionary<string, TaskCompletionSource<TResponse>> pendingInteractions, Exception exception)
    {
        TaskCompletionSource<TResponse>[] completions;
        lock (myInteractionLock)
        {
            completions = pendingInteractions.Values.ToArray();
            pendingInteractions.Clear();
        }
        foreach (var completion in completions)
            completion.TrySetException(exception);
    }

    private static void CompleteWithException<TResponse>(
        IEnumerable<TaskCompletionSource<TResponse>> completions,
        Exception exception)
    {
        foreach (var completion in completions)
            completion.TrySetException(exception);
    }

    private static string CreateInteractionId() => Guid.NewGuid().ToString("N");

    private static string GetRequestId(AgentEvent request) => request switch
    {
        AgentPermissionRequest permission => permission.RequestId,
        AgentInputRequest input => input.RequestId,
        AgentElicitationRequest elicitation => elicitation.RequestId,
        _ => throw new ArgumentException("Expected an interaction request.", nameof(request)),
    };
}



