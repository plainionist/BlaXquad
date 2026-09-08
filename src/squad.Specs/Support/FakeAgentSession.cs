using System.Text.Json;
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
/// assistant reply through the real transcript without any product test hook. Every other host-driven session
/// member (harness messages, aborts, and interaction responses) reports its own generic observation across the
/// same control transport when one is configured, and <see cref="Emit"/> publishes whichever real production
/// <c>AgentEvent</c> (or session completion/failure) a "emit" command pushed across the pipe names.
/// </summary>
internal sealed class FakeAgentSession : IAgentSession, IAgentReadinessProbe
{
    private readonly AgentEventChannel myEvents = new();
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FakeProviderControlClient? myControl;
    private TaskCompletionSource<string>? myPendingReply;
    private long myGeneration;
    private bool myRejectNextHarness;

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

    /// <summary>Publishes the real production <see cref="AgentHarnessMessageEvent"/> for host-authored context -
    /// the same way a real provider preserves it as a distinct harness message in the transcript - and, when a
    /// control transport is configured, reports it as a generic observation so a scenario can wait for it directly
    /// across the pipe. When <see cref="RejectNextHarness"/> armed this session to reject its next harness send,
    /// reports the rejected attempt as its own generic observation (so a scenario can wait for the host to have
    /// observably attempted and failed the send, rather than merely snapshotting the absence of a successful one)
    /// and throws instead of publishing anything - simulating a real provider connection that never accepts the
    /// host-authored message, exactly once.</summary>
    public async Task SendHarnessAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (myRejectNextHarness)
        {
            myRejectNextHarness = false;
            if (myControl is not null)
            {
                await myControl.NotifyObservationAsync(Role, SessionId, "harness-rejected", new { content = prompt }, cancellationToken);
            }
            throw new InvalidOperationException("The fake provider rejected this harness send.");
        }

        myEvents.Publish(new AgentHarnessMessageEvent(DateTimeOffset.UtcNow, prompt));
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(Role, SessionId, "harness-message", new { content = prompt }, cancellationToken);
        }
    }

    /// <summary>Arms this session to reject its very next harness send with an exception instead of publishing or
    /// reporting it - a test-only control (not a production <c>AgentEvent</c>) used to prove that a single failed
    /// notification does not lose durable delivery state or destabilize the host.</summary>
    public void RejectNextHarness() => myRejectNextHarness = true;

    /// <summary>Reports, when a control transport is configured, that the host aborted this session's current
    /// operation.</summary>
    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(Role, SessionId, "abort", new { }, cancellationToken);
        }
    }

    /// <summary>Reports, when a control transport is configured, the host's response to a permission request this
    /// session previously emitted through <see cref="Emit"/>.</summary>
    public async Task RespondToPermissionAsync(
        string requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(
                Role, SessionId, "permission-response", new { requestId, approved = response.Approved }, cancellationToken);
        }
    }

    /// <summary>Reports, when a control transport is configured, the host's response to an input request this
    /// session previously emitted through <see cref="Emit"/>.</summary>
    public async Task RespondToInputAsync(
        string requestId, AgentInputResponse response, CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(
                Role, SessionId, "input-response",
                new { requestId, answer = response.Answer, wasFreeform = response.WasFreeform }, cancellationToken);
        }
    }

    /// <summary>Reports, when a control transport is configured, the host's response to an elicitation request
    /// this session previously emitted through <see cref="Emit"/>.</summary>
    public async Task RespondToElicitationAsync(
        string requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(
                Role, SessionId, "elicitation-response", new { requestId, action = response.Action }, cancellationToken);
        }
    }

    public Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Publishes the real production <see cref="AgentEvent"/> (or completes/fails this session) named by an
    /// "emit" command received across the fake-provider control pipe, returning an explicit diagnostic instead of
    /// throwing if the given kind is not supported - so <see cref="FakeAgentRuntime"/> can turn it into a protocol
    /// error the client observes instead of crashing the launched process.
    /// </summary>
    public string? Emit(string kind, JsonElement data)
    {
        var now = DateTimeOffset.UtcNow;
        switch (kind)
        {
            case "reasoning":
                myEvents.Publish(new AgentReasoningEvent(now, data.GetProperty("content").GetString()!, data.GetProperty("isDelta").GetBoolean()));
                return null;
            case "tool-started":
                myEvents.Publish(new AgentToolStartedEvent(
                    now,
                    data.GetProperty("toolCallId").GetString()!,
                    data.GetProperty("toolName").GetString()!,
                    GetNullableString(data, "arguments"),
                    GetNullableString(data, "toolKind"),
                    GetNullableString(data, "workingDirectory")));
                return null;
            case "tool-progress":
                myEvents.Publish(new AgentToolProgressEvent(
                    now, data.GetProperty("toolCallId").GetString()!, data.GetProperty("progress").GetString()!));
                return null;
            case "tool-output-changed":
                myEvents.Publish(new AgentToolOutputChangedEvent(
                    now, data.GetProperty("toolCallId").GetString()!, data.GetProperty("output").GetString()!));
                return null;
            case "tool-completed":
                myEvents.Publish(new AgentToolCompletedEvent(
                    now,
                    data.GetProperty("toolCallId").GetString()!,
                    data.GetProperty("toolName").GetString()!,
                    data.GetProperty("succeeded").GetBoolean(),
                    GetNullableString(data, "displayOutputFallback"),
                    GetNullableString(data, "contentFallback")));
                return null;
            case "readiness":
                myEvents.Publish(new AgentReadinessEvent(
                    now, NextGeneration(), data.GetProperty("state").GetString()!, GetNullableString(data, "error")));
                return null;
            case "usage":
                myEvents.Publish(new AgentSessionUsageEvent(now, data.GetProperty("aicUsed").GetDecimal()));
                return null;
            case "context-usage":
                myEvents.Publish(new AgentContextUsageEvent(
                    now, data.GetProperty("usedTokens").GetInt64(), data.GetProperty("limitTokens").GetInt64()));
                return null;
            case "permission-request":
                myEvents.Publish(new AgentPermissionRequest(
                    now, data.GetProperty("requestId").GetString()!, Role, data.GetProperty("description").GetString()!));
                return null;
            case "input-request":
                myEvents.Publish(new AgentInputRequest(
                    now,
                    data.GetProperty("requestId").GetString()!,
                    Role,
                    data.GetProperty("prompt").GetString()!,
                    GetNullableStringArray(data, "choices"),
                    !data.TryGetProperty("allowFreeform", out var allowFreeform) || allowFreeform.ValueKind != JsonValueKind.False));
                return null;
            case "elicitation-request":
                myEvents.Publish(new AgentElicitationRequest(
                    now,
                    data.GetProperty("requestId").GetString()!,
                    Role,
                    data.GetProperty("prompt").GetString()!,
                    data.GetProperty("mode").GetString()!,
                    null,
                    GetNullableString(data, "url")));
                return null;
            case "idle":
                myEvents.Publish(new AgentIdleEvent(now));
                return null;
            case "reject-next-harness":
                RejectNextHarness();
                return null;
            case "complete-session":
                myEvents.Publish(new AgentStoppedEvent(now));
                myCompletion.TrySetResult();
                return null;
            case "fail-session":
                var message = data.GetProperty("message").GetString()!;
                myEvents.Publish(new AgentErrorEvent(now, message));
                myCompletion.TrySetException(new InvalidOperationException(message));
                return null;
            default:
                return $"Unknown emit kind '{kind}'.";
        }
    }

    private static string? GetNullableString(JsonElement data, string name) =>
        data.TryGetProperty(name, out var element) && element.ValueKind != JsonValueKind.Null ? element.GetString() : null;

    private static IReadOnlyList<string>? GetNullableStringArray(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        return element.EnumerateArray().Select(item => item.GetString()!).ToList();
    }

    private long NextGeneration() => Interlocked.Increment(ref myGeneration);

    /// <summary>Reports whether the given generation is still this session's latest minted readiness generation -
    /// the admission check production <see cref="squad.Application.SquadViewModel"/> uses to discard a stale
    /// readiness observation instead of letting it overwrite newer operation state.</summary>
    public bool IsReadinessGenerationCurrent(long generation) => Interlocked.Read(ref myGeneration) == generation;

    /// <summary>Advances this session's readiness generation so any readiness observation already computed under
    /// an earlier generation can no longer be mistaken for current - mirroring production's own invalidation
    /// before dispatching a new prompt.</summary>
    public void InvalidateReadiness() => Interlocked.Increment(ref myGeneration);

    /// <summary>This fake session has no independent readiness probe of its own to consult - every readiness
    /// observation it reports arrives explicitly through <see cref="Emit"/>.</summary>
    public Task<AgentReadinessEvent?> ObserveReadinessAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<AgentReadinessEvent?>(null);

    public async ValueTask DisposeAsync()
    {
        IsDisposed = true;
        myPendingReply?.TrySetCanceled();
        myEvents.Complete();
        myCompletion.TrySetResult();
        await myEvents.DisposeAsync();
    }
}


