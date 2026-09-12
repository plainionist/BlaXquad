using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Domain;
using System.Diagnostics;
using System.Text.Json;

namespace squad.AgentProvider.CopilotSdk;

/// <summary>
/// Adapts one Copilot SDK session to the provider-neutral event and interaction contract. It owns pending
/// interaction completion, event backpressure, usage refreshes, and teardown of the attached SDK session.
/// </summary>
internal sealed class CopilotSdkAgentSession : IAgentSession
{
    private static readonly TimeSpan myDefaultFailureTeardownTimeout = TimeSpan.FromSeconds(5);
    private readonly AgentEventChannel myEvents;
    private readonly TimeSpan myFailureTeardownTimeout = myDefaultFailureTeardownTimeout;
    private readonly Action<Exception>? myEscalateTeardownFailure;
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object myInteractionLock = new();
    private readonly object myContextUsageLock = new();
    private readonly Dictionary<string, TaskCompletionSource<AgentPermissionResponse>> myPendingPermissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<AgentInputResponse>> myPendingInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<AgentElicitationResponse>> myPendingElicitations = new(StringComparer.Ordinal);
    private readonly Queue<string> myPendingHarnessMessageEchoes = new();
    private readonly TaskCompletionSource myFailureTeardown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CopilotSdkRuntimeSession? myRuntimeSession;
    private UsageRefreshCoordinator? myUsageRefresh;
    private Task myContextLimitResolution = Task.CompletedTask;
    private long? myContextUsedTokens;
    private long? myContextLimitTokens;
    private Exception? myFailure;
    private bool myDisposed;

    public CopilotSdkAgentSession(SquadMemberId memberId, Action<Exception>? escalateTeardownFailure = null)
    {
        MemberId = memberId;
        myEscalateTeardownFailure = escalateTeardownFailure;
        myEvents = new AgentEventChannel(FailSession);
    }

    public SquadMemberId MemberId { get; }
    public string SessionId => myRuntimeSession?.SessionId ?? throw new InvalidOperationException("Copilot session has not been created");
    public Task Completion => myCompletion.Task;

    internal void Attach(CopilotSdkRuntimeSession runtimeSession)
    {
        Contract.Invariant(myRuntimeSession is null, "A Copilot SDK session can be attached at most once.");
        myRuntimeSession = runtimeSession;
        myContextLimitResolution = ResolveContextLimitAsync(runtimeSession);
        myUsageRefresh = new UsageRefreshCoordinator(RefreshUsageAsync);
        myUsageRefresh.RunInitialRefresh();
    }

    public void Publish(AgentEvent agentEvent) => myEvents.Publish(agentEvent);

    public Task PublishAsync(AgentEvent agentEvent, CancellationToken cancellationToken = default) =>
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
            {
                return false;
            }
            myPendingHarnessMessageEchoes.Dequeue();
            return true;
        }
    }

    /// <summary>Routes one raw SDK event through the usage refresh coordinator ahead of normal event translation.</summary>
    internal void NotifyUsageActivity(bool isIdle)
    {
        if (myDisposed || myFailure is not null)
        {
            return;
        }
        if (isIdle)
        {
            myUsageRefresh?.NotifyIdle();
        }
        else
        {
            myUsageRefresh?.NotifyActivity();
        }
    }

    internal void NotifyContextUsage(long currentTokens)
    {
        lock (myContextUsageLock)
        {
            if (myDisposed || myFailure is not null)
            {
                return;
            }
            myContextUsedTokens = currentTokens;
            PublishContextUsage();
        }
    }

    /// <summary>
    /// Aborts the provider operation and cancels all pending interactions. Successful abort does not terminate the
    /// session, which remains available for later prompts.
    /// </summary>
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
        RequestInteractionAsync(new AgentPermissionRequest(DateTimeOffset.UtcNow, CreateInteractionId(), MemberId.Value, description), myPendingPermissions, cancellationToken);

    internal Task<AgentInputResponse> RequestInputAsync(string prompt, IReadOnlyList<string>? choices, bool allowFreeform, CancellationToken cancellationToken = default) =>
        RequestInteractionAsync(new AgentInputRequest(DateTimeOffset.UtcNow, CreateInteractionId(), MemberId.Value, prompt, choices, allowFreeform), myPendingInputs, cancellationToken);

    internal Task<AgentElicitationResponse> RequestElicitationAsync(string prompt, string mode, JsonElement? requestedSchema, string? url, CancellationToken cancellationToken = default) =>
        RequestInteractionAsync(new AgentElicitationRequest(DateTimeOffset.UtcNow, CreateInteractionId(), MemberId.Value, prompt, mode, requestedSchema, url), myPendingElicitations, cancellationToken);

    public async IAsyncEnumerable<AgentEvent> Events([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var agentEvent in myEvents.ReadAllAsync(cancellationToken))
        {
            yield return agentEvent;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (myDisposed)
        {
            return;
        }
        lock (myInteractionLock)
        {
            if (myDisposed)
            {
                return;
            }
            myDisposed = true;
        }
        try
        {
            CancelPendingInteractionsCore(CancellationToken.None);
            if (myUsageRefresh is not null)
            {
                await myUsageRefresh.DisposeAsync();
            }
            await myContextLimitResolution;
            if (myFailure is not null)
            {
                await myFailureTeardown.Task;
            }
            if (myRuntimeSession is not null)
            {
                var disposal = myRuntimeSession.DisposeAsync().AsTask();
                if (myFailure is not null)
                {
                    await disposal.WaitAsync(myFailureTeardownTimeout);
                }
                else
                {
                    await disposal;
                }
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

    private async Task ResolveContextLimitAsync(CopilotSdkRuntimeSession runtimeSession)
    {
        try
        {
            var contextLimit = await runtimeSession.GetContextLimitAsync().ConfigureAwait(false);
            lock (myContextUsageLock)
            {
                if (myDisposed || myFailure is not null || contextLimit is not > 0)
                {
                    return;
                }
                myContextLimitTokens = contextLimit;
                PublishContextUsage();
            }
        }
        catch
        {
        }
    }

    private void PublishContextUsage()
    {
        if (myContextUsedTokens is { } usedTokens && myContextLimitTokens is { } limitTokens)
        {
            Publish(new AgentContextUsageEvent(DateTimeOffset.UtcNow, usedTokens, limitTokens));
        }
    }

    private async Task RefreshUsageAsync(CancellationToken cancellationToken)
    {
        var runtimeSession = myRuntimeSession;
        if (runtimeSession is null || myDisposed || myFailure is not null)
        {
            return;
        }
        var usage = await runtimeSession.GetAicUsageAsync(cancellationToken);
        if (!myDisposed && myFailure is null)
        {
            Publish(new AgentSessionUsageEvent(DateTimeOffset.UtcNow, usage));
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
            {
                return;
            }
            myFailure = exception;
            permissions = myPendingPermissions.Values.ToArray();
            inputs = myPendingInputs.Values.ToArray();
            elicitations = myPendingElicitations.Values.ToArray();
            myPendingPermissions.Clear();
            myPendingInputs.Clear();
            myPendingElicitations.Clear();
        }

        myEvents.Complete(exception);
        _ = TeardownAfterFailureAsync(teardownRuntimeSession);
        myCompletion.TrySetException(exception);
        CompleteWithException(permissions, exception);
        CompleteWithException(inputs, exception);
        CompleteWithException(elicitations, exception);
    }

    /// <summary>
    /// Stops the usage refresh coordinator the same way disposal does — cancelling pending delays, preventing new
    /// RPC work, and observing any in-flight refresh — before proceeding with runtime-session teardown. This runs
    /// ahead of the runtime-session teardown so a scheduled refresh can never publish into the now-completed event
    /// channel.
    /// </summary>
    private async Task TeardownAfterFailureAsync(bool teardownRuntimeSession)
    {
        if (myUsageRefresh is not null)
        {
            await myUsageRefresh.DisposeAsync().ConfigureAwait(false);
        }
        await myContextLimitResolution.ConfigureAwait(false);

        if (teardownRuntimeSession)
        {
            await TeardownFailedRuntimeSessionAsync().ConfigureAwait(false);
        }
        else
        {
            myFailureTeardown.TrySetResult();
        }
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
                $"Failed to stop overloaded Copilot SDK member '{MemberId}' within the teardown grace period.",
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
            {
                throw failure;
            }
            if (myDisposed)
            {
                throw new ObjectDisposedException(nameof(CopilotSdkAgentSession));
            }
        }
    }

    private async Task<TResponse> RequestInteractionAsync<TResponse>(AgentEvent request, Dictionary<string, TaskCompletionSource<TResponse>> pendingInteractions, CancellationToken cancellationToken)
    {
        var requestId = GetRequestId(request);
        var completion = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (myInteractionLock)
        {
            if (myFailure is { } failure)
            {
                throw failure;
            }
            if (myDisposed)
            {
                throw new ObjectDisposedException(nameof(CopilotSdkAgentSession));
            }
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
            {
                throw failure;
            }
            if (!pendingInteractions.Remove(requestId, out completion!))
            {
                throw new InvalidOperationException($"No pending interaction with ID '{requestId}' exists for role '{MemberId}'.");
            }
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
        {
            completion.TrySetCanceled(cancellationToken);
        }
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
        {
            completion.TrySetException(exception);
        }
    }

    private static void CompleteWithException<TResponse>(
        IEnumerable<TaskCompletionSource<TResponse>> completions,
        Exception exception)
    {
        foreach (var completion in completions)
        {
            completion.TrySetException(exception);
        }
    }

    private static string CreateInteractionId() => Guid.NewGuid().ToString("N");

    private static string GetRequestId(AgentEvent request)
    {
        Contract.Requires(
            request is AgentPermissionRequest or AgentInputRequest or AgentElicitationRequest,
            "Expected an interaction request.");
        return request switch
        {
            AgentPermissionRequest permission => permission.RequestId,
            AgentInputRequest input => input.RequestId,
            AgentElicitationRequest elicitation => elicitation.RequestId,
            _ => throw new UnreachableException(),
        };
    }
}

