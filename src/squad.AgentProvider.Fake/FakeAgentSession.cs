using System.Text.Json;
using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.AgentProvider.Fake.Control;
using squad.Domain;

namespace squad.AgentProvider.Fake;

/// <summary>
/// Provider-side session for <see cref="FakeAgentProviderFactory"/>. Establishes itself by publishing the real
/// "started" provider event through its own test-owned event stream. Without a control transport, <see cref="SendAsync"/>
/// mirrors the minimal Slice 6 behavior (publish the user message, then go idle) with no prompt handling,
/// permission, or elicitation behavior. With a control transport, it instead reports the prompt across the pipe
/// and awaits a semantic reply delivered through <see cref="DeliverReply"/>, publishing it as the real production
/// <see cref="AgentAssistantMessageEvent"/> before going idle - this is what lets a black-box scenario drive an
/// assistant reply through the real transcript without any product test hook. Every other host-driven session
/// member (harness messages, aborts, and interaction responses) reports its own generic observation across the
/// same control transport when one is configured, and <see cref="Emit"/> publishes whichever real production
/// <c>AgentEvent</c> (or session completion/failure) a "emit" command pushed across the pipe names.
/// <see cref="AbortAsync"/> defaults to immediate success, but "emit" commands can arm it to remain pending or to
/// fail on its very next call, giving a scenario deterministic, acknowledged control over an abort's outcome
/// without any arbitrary sleep.
/// </summary>
internal sealed class FakeAgentSession : IAgentSession
{
    private readonly TaskCompletionSource myCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TestAgentEventStream myEvents = new();
    private readonly FakeProviderControlClient? myControl;
    private TaskCompletionSource<string>? myPendingReply;
    private bool mySendCanceledBeforeDisposal;
    private bool myRejectNextHarness;
    private TaskCompletionSource? myPendingAbort;
    private string? myNextAbortFailureMessage;
    private TaskCompletionSource? myPendingDisposal;
    private bool myAutoEcho;

    public FakeAgentSession(SquadMemberId memberId, FakeProviderControlClient? control = null)
    {
        MemberId = memberId;
        myControl = control;
        myEvents.Publish(new AgentStartedEvent(DateTimeOffset.UtcNow));
    }

    public SquadMemberId MemberId { get; }
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

        if (myAutoEcho)
        {
            await myControl.NotifyPromptAsync(MemberId.Value, SessionId, prompt, cancellationToken);
            myEvents.Publish(new AgentAssistantMessageEvent(DateTimeOffset.UtcNow, $"echo: {prompt}", IsDelta: false));
            myEvents.Publish(new AgentIdleEvent(DateTimeOffset.UtcNow));
            return;
        }

        var pendingReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        myPendingReply = pendingReply;
        await myControl.NotifyPromptAsync(MemberId.Value, SessionId, prompt, cancellationToken);
        // A real provider's send observes its cancellation token instead of blocking forever once the host
        // decides to give up on this round trip (for example during shutdown); mirror that here so a scenario
        // can prove the host itself remains well-behaved under a still-outstanding prompt, without this fixture
        // manufacturing an unrealistic, uncancelable wait no real provider would exhibit.
        string content;
        try
        {
            content = await pendingReply.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Records this send's own terminal, canceled outcome in-process, synchronously, before propagating -
            // not merely a control-pipe notification a scenario could observe in either order relative to the
            // later "disposal-held" notification below. DisposeAsync reads this same field when it later runs,
            // so a scenario that observes "disposal-held" carrying this flag as true has proof - not an
            // inference from production's own call sequence, and not reliant on any pipe message ordering - that
            // this admitted send already reached its canceled outcome strictly before disposal began.
            mySendCanceledBeforeDisposal = true;
            throw;
        }
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
                await myControl.NotifyObservationAsync(MemberId.Value, SessionId, "harness-rejected", new { content = prompt }, cancellationToken);
            }
            throw new InvalidOperationException("The fake provider rejected this harness send.");
        }

        myEvents.Publish(new AgentHarnessMessageEvent(DateTimeOffset.UtcNow, prompt));
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(MemberId.Value, SessionId, "harness-message", new { content = prompt }, cancellationToken);
        }
    }

    /// <summary>Arms this session to reject its very next harness send with an exception instead of publishing or
    /// reporting it - a test-only control (not a production <c>AgentEvent</c>) used to prove that a single failed
    /// notification does not lose durable delivery state or destabilize the host.</summary>
    public void RejectNextHarness() => myRejectNextHarness = true;

    /// <summary>Test-only control (not a production event): arms this session so every future <see cref="SendAsync"/>
    /// answers its prompt automatically with "echo: {prompt}" - reporting the prompt across the control pipe exactly
    /// like an ordinary send, but completing immediately instead of waiting for an explicit <see cref="DeliverReply"/>.
    /// This is the same automatic reply behavior the stdio UI protocol black-box scenarios' readiness-only fixture
    /// once hardcoded, exposed through the shared fake-provider control transport so real-wire-framing
    /// specifications do not need a second, narrower provider fixture.</summary>
    public void EnableAutoEcho() => myAutoEcho = true;

    /// <summary>Reports, when a control transport is configured, that the host aborted this session's current
    /// operation. By default the abort completes immediately, matching every scenario that never arms one of the
    /// controls below. If <see cref="ArmPendingAbort"/> armed this session first, the abort remains pending until
    /// <see cref="CompletePendingAbort"/> or <see cref="FailPendingAbort"/> resolves it - proving a following
    /// prompt genuinely waits for an in-flight cancellation instead of racing it. If <see cref="FailNextAbort"/>
    /// armed this session first, the abort fails immediately instead - proving a failed abort's user-visible
    /// outcome.</summary>
    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(MemberId.Value, SessionId, "abort", new { }, cancellationToken);
        }

        var failureMessage = Interlocked.Exchange(ref myNextAbortFailureMessage, null);
        if (failureMessage is not null)
        {
            throw new InvalidOperationException(failureMessage);
        }

        // Keeps the field set (rather than clearing it up front) so a concurrent "complete pending abort" or
        // "fail pending abort" control - which resolves through this same field - can still find and resolve the
        // very instance this call is awaiting, then clears it (only if a newer arming has not since replaced it).
        var pendingAbort = myPendingAbort;
        if (pendingAbort is not null)
        {
            try
            {
                await pendingAbort.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.CompareExchange(ref myPendingAbort, null, pendingAbort);
            }
        }
    }

    /// <summary>Test-only control (not a production event): arms this session so its very next
    /// <see cref="AbortAsync"/> call remains pending until <see cref="CompletePendingAbort"/> or
    /// <see cref="FailPendingAbort"/> resolves it - the deterministic control a scenario needs to prove abort
    /// in-flight behavior (a following prompt waiting for it to finish) without an arbitrary sleep.</summary>
    public void ArmPendingAbort() => myPendingAbort = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Resolves this session's currently pending abort (armed by <see cref="ArmPendingAbort"/>) as
    /// successful.</summary>
    public void CompletePendingAbort() => myPendingAbort?.TrySetResult();

    /// <summary>Resolves this session's currently pending abort (armed by <see cref="ArmPendingAbort"/>) as
    /// failed with the given message.</summary>
    public void FailPendingAbort(string message) => myPendingAbort?.TrySetException(new InvalidOperationException(message));

    /// <summary>Test-only control (not a production event): arms this session so its very next
    /// <see cref="AbortAsync"/> call fails immediately with the given message instead of succeeding - proving a
    /// failed abort's user-visible outcome without any pending, held-open state.</summary>
    public void FailNextAbort(string message) => myNextAbortFailureMessage = message;

    /// <summary>Test-only control (not a production event): arms this session so its disposal, when it happens,
    /// first reports a "disposal-held" observation and then remains pending until
    /// <see cref="CompletePendingDisposal"/> resolves it - the deterministic control a scenario needs to prove
    /// backend cleanup genuinely holds at the real provider boundary (for example so it can prove a command
    /// arriving after admission closes still gets rejected while cleanup is in progress) without any arbitrary
    /// sleep.</summary>
    public void ArmPendingDisposal() => myPendingDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Resolves this session's currently held disposal (armed by <see cref="ArmPendingDisposal"/>),
    /// letting it proceed.</summary>
    public void CompletePendingDisposal() => myPendingDisposal?.TrySetResult();

    /// <summary>Reports, when a control transport is configured, the host's response to a permission request this
    /// session previously emitted through <see cref="Emit"/>.</summary>
    public async Task RespondToPermissionAsync(
        InteractionRequestId requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(
                MemberId.Value, SessionId, "permission-response", new { requestId = requestId.Value, approved = response.Approved }, cancellationToken);
        }
    }

    /// <summary>Reports, when a control transport is configured, the host's response to an input request this
    /// session previously emitted through <see cref="Emit"/>.</summary>
    public async Task RespondToInputAsync(
        InteractionRequestId requestId, AgentInputResponse response, CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(
                MemberId.Value, SessionId, "input-response",
                new { requestId = requestId.Value, answer = response.Answer, wasFreeform = response.WasFreeform }, cancellationToken);
        }
    }

    /// <summary>Reports, when a control transport is configured, the host's response to an elicitation request
    /// this session previously emitted through <see cref="Emit"/>.</summary>
    public async Task RespondToElicitationAsync(
        InteractionRequestId requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(
                MemberId.Value, SessionId, "elicitation-response",
                new { requestId = requestId.Value, action = response.Action, content = response.Content }, cancellationToken);
        }
    }

    /// <summary>Reports, when a control transport is configured, that the host cancelled this session's pending
    /// interactions (for example while stopping with a request still outstanding).</summary>
    public async Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default)
    {
        if (myControl is not null)
        {
            await myControl.NotifyObservationAsync(MemberId.Value, SessionId, "pending-interactions-cancelled", new { }, cancellationToken);
        }
    }

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
            case "assistant":
                myEvents.Publish(new AgentAssistantMessageEvent(now, data.GetProperty("content").GetString()!, data.GetProperty("isDelta").GetBoolean()));
                return null;
            case "system-message":
                myEvents.Publish(new AgentSystemMessageEvent(now, data.GetProperty("content").GetString()!));
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
            case "usage":
                myEvents.Publish(new AgentSessionUsageEvent(now, data.GetProperty("aicUsed").GetDecimal()));
                return null;
            case "context-usage":
                myEvents.Publish(new AgentContextUsageEvent(
                    now, data.GetProperty("usedTokens").GetInt64(), data.GetProperty("limitTokens").GetInt64()));
                return null;
            case "permission-request":
                myEvents.Publish(new AgentPermissionRequest(
                    now, new InteractionRequestId(data.GetProperty("requestId").GetString()!), data.GetProperty("description").GetString()!));
                return null;
            case "input-request":
                myEvents.Publish(new AgentInputRequest(
                    now,
                    new InteractionRequestId(data.GetProperty("requestId").GetString()!),
                    data.GetProperty("prompt").GetString()!,
                    GetNullableStringArray(data, "choices"),
                    !data.TryGetProperty("allowFreeform", out var allowFreeform) || allowFreeform.ValueKind != JsonValueKind.False));
                return null;
            case "elicitation-request":
                myEvents.Publish(new AgentElicitationRequest(
                    now,
                    new InteractionRequestId(data.GetProperty("requestId").GetString()!),
                    data.GetProperty("prompt").GetString()!,
                    ParseElicitationMode(data.GetProperty("mode").GetString()),
                    null,
                    GetNullableString(data, "url")));
                return null;
            case "subagent-started":
                myEvents.Publish(new AgentSubagentStartedEvent(
                    now,
                    GetNullableString(data, "agentName"),
                    GetNullableString(data, "agentDisplayName"),
                    GetNullableString(data, "model")));
                return null;
            case "skill-invoked":
                myEvents.Publish(new AgentSkillInvokedEvent(now, data.GetProperty("name").GetString()!));
                return null;
            case "idle":
                myEvents.Publish(new AgentIdleEvent(now));
                return null;
            case "reject-next-harness":
                RejectNextHarness();
                return null;
            case "enable-auto-echo":
                EnableAutoEcho();
                return null;
            case "arm-pending-abort":
                ArmPendingAbort();
                return null;
            case "complete-pending-abort":
                CompletePendingAbort();
                return null;
            case "fail-pending-abort":
                FailPendingAbort(data.GetProperty("message").GetString()!);
                return null;
            case "fail-next-abort":
                FailNextAbort(data.GetProperty("message").GetString()!);
                return null;
            case "arm-pending-disposal":
                ArmPendingDisposal();
                return null;
            case "complete-pending-disposal":
                CompletePendingDisposal();
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

    /// <summary>Maps the fake-control envelope's elicitation mode spelling into <see cref="ElicitationMode"/> at
    /// this control-pipe input boundary, rejecting any unsupported spelling explicitly rather than carrying it
    /// inward.</summary>
    private static ElicitationMode ParseElicitationMode(string? mode) => mode switch
    {
        "form" => ElicitationMode.Form,
        "url" => ElicitationMode.Url,
        _ => throw new InvalidOperationException($"Unsupported elicitation mode '{mode}' from the fake-control envelope."),
    };

    private static IReadOnlyList<string>? GetNullableStringArray(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        return element.EnumerateArray().Select(item => item.GetString()!).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        // If a scenario armed a held disposal for this session, report it before waiting so a scenario can prove
        // backend cleanup has genuinely reached this real provider boundary - not merely infer it from timing -
        // before it proceeds to prove a command arriving while cleanup remains held is still rejected. The
        // observation carries this session's own in-process record of whether an admitted send already reached
        // its canceled outcome, giving a scenario a direct causal proof of drain-before-dispose ordering instead
        // of one built on control-pipe message arrival order.
        var pendingDisposal = myPendingDisposal;
        if (pendingDisposal is not null)
        {
            if (myControl is not null)
            {
                await myControl.NotifyObservationAsync(
                    MemberId.Value, SessionId, "disposal-held", new { sendCanceledBeforeDisposal = mySendCanceledBeforeDisposal }, CancellationToken.None);
            }
            await pendingDisposal.Task;
        }

        IsDisposed = true;
        myPendingReply?.TrySetCanceled();
        myEvents.Complete();
        myCompletion.TrySetResult();
        await myEvents.DisposeAsync();
    }
}





