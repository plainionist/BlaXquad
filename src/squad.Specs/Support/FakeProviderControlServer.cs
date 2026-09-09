using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>
/// Test-runner end of the private fake-provider control transport: a uniquely named local named pipe whose name
/// and random per-scenario authentication token travel to the launched squad-hq process only through the
/// <see cref="PipeNameEnvironmentVariable"/> and <see cref="TokenEnvironmentVariable"/> environment variables -
/// squad-hq itself never parses, forwards, or otherwise knows about this channel. Every exchange is one typed,
/// versioned, newline-delimited JSON envelope carrying a correlation id; the shared <see cref="ControlPipeDuplex"/>
/// gives this endpoint exactly one background dispatch loop, so concurrent session observations and their
/// acknowledgements can never compete to read the pipe.
/// </summary>
public sealed class FakeProviderControlServer : IAsyncDisposable
{
    public const string PipeNameEnvironmentVariable = "BLAXQUAD_FAKE_CONTROL_PIPE";
    public const string TokenEnvironmentVariable = "BLAXQUAD_FAKE_CONTROL_TOKEN";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly NamedPipeServerStream myPipe;
    private readonly object myStateLock = new();
    private readonly List<(string Role, string Type, string SessionId)> myObservations = [];
    private readonly List<string> myProtocolErrors = [];
    private readonly Dictionary<string, string> myActiveSessionByRole = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> myLatestPromptByRole = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Role, string Kind), JsonElement> myLatestObservationByRoleAndKind = new();
    private readonly Dictionary<(string Role, string Kind), int> myObservationCountsByRoleAndKind = new();
    private ControlPipeDuplex? myDuplex;
    private bool myAuthenticated;

    private FakeProviderControlServer(string pipeName, string token)
    {
        PipeName = pipeName;
        Token = token;
        myPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    /// <summary>The unique per-scenario pipe name, published to the launched process only via
    /// <see cref="PipeNameEnvironmentVariable"/>.</summary>
    public string PipeName { get; }

    /// <summary>The random per-scenario authentication token every connecting client must present on
    /// "connect", published to the launched process only via <see cref="TokenEnvironmentVariable"/>.</summary>
    public string Token { get; }

    /// <summary>Creates one server bound to a freshly generated, globally unique pipe name and a random token.</summary>
    public static FakeProviderControlServer Create() =>
        new($"blaxquad-fake-control-{Guid.NewGuid():N}", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    /// <summary>Waits for the provider-process client to connect and starts the one background dispatch loop
    /// that owns every subsequent read from the pipe.</summary>
    public async Task WaitForConnectionAsync(TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout ?? DefaultTimeout);
        try
        {
            await myPipe.WaitForConnectionAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new FakeProviderControlTimeoutException(
                "a client to connect to the fake-provider control pipe", DescribeDiagnostics(additionalDiagnostics));
        }
        myDuplex = new ControlPipeDuplex(myPipe, HandleUnsolicitedAsync);
        myDuplex.StartDispatching();
    }

    /// <summary>Waits until the connected client has reported (and this server has acknowledged) that the given
    /// role's session started.</summary>
    public Task WaitForSessionStartedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForObservationAsync(role, "session-started", timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported (and this server has acknowledged) that the given
    /// role's session was disposed.</summary>
    public Task WaitForSessionDisposedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForObservationAsync(role, "session-disposed", timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported a prompt sent to the given role, and returns its
    /// content.</summary>
    public async Task<string> WaitForPromptAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (myStateLock)
            {
                if (myLatestPromptByRole.TryGetValue(role, out var prompt))
                {
                    return prompt;
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report a prompt across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until the connected client has reported a prompt sent to the given role whose content
    /// satisfies the given predicate, and returns it. Unlike <see cref="WaitForPromptAsync(string,TimeSpan?,Func{string}?)"/>,
    /// this keeps polling past an already-observed prompt that does not satisfy the predicate (such as an earlier
    /// prompt for the same role, sent before this one was serialized behind it), so a caller can distinguish a
    /// later, distinct prompt from that earlier one.</summary>
    public async Task<string> WaitForPromptAsync(
        string role, Func<string, bool> matches, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var prompt = LatestPrompt(role);
            if (prompt is not null && matches(prompt))
            {
                return prompt;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report a matching prompt across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Returns the content of the most recent prompt this role's session has reported across the control
    /// pipe, or null if none has been reported yet - a snapshot read (no waiting) used to prove the absence of a
    /// prompt, or that a role's latest observed prompt has not yet advanced past an earlier one, rather than the
    /// presence of a later one.</summary>
    public string? LatestPrompt(string role)
    {
        lock (myStateLock)
        {
            return myLatestPromptByRole.TryGetValue(role, out var prompt) ? prompt : null;
        }
    }

    /// <summary>Waits until the connected client has reported the host sending this role's session its initial
    /// harness instruction, and returns its content.</summary>
    public async Task<string> WaitForHarnessMessageAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var data = await WaitForObservationDataAsync(role, "harness-message", timeout, additionalDiagnostics);
        return data.GetProperty("content").GetString()!;
    }

    /// <summary>Waits until the connected client has reported this role's session receiving a harness message
    /// whose content satisfies the given predicate. Unlike <see cref="WaitForHarnessMessageAsync(string,TimeSpan?,Func{string}?)"/>,
    /// this keeps polling past an already-observed harness message that does not satisfy the predicate (such as
    /// the role's own initial instruction, sent once at session start), so a caller can distinguish a later,
    /// distinct harness message - for example a delivery wake-up - from that earlier one.</summary>
    public async Task<string> WaitForHarnessMessageAsync(
        string role, Func<string, bool> matches, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var content = LatestHarnessMessage(role);
            if (content is not null && matches(content))
            {
                return content;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report a matching harness message across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Returns the content of the most recent harness message this role's session has reported across
    /// the control pipe, or null if none has been reported yet - a snapshot read (no waiting) used to prove the
    /// absence of a later harness message rather than the presence of one.</summary>
    public string? LatestHarnessMessage(string role)
    {
        lock (myStateLock)
        {
            return myLatestObservationByRoleAndKind.TryGetValue((role, "harness-message"), out var data)
                ? data.GetProperty("content").GetString()
                : null;
        }
    }

    /// <summary>Waits until the connected client has reported this role's session rejecting a harness send (armed
    /// by a prior test-only "reject next harness" control), and returns its content - proving the host has
    /// observably attempted and failed that send, rather than only that a successful one was never observed.</summary>
    public async Task<string> WaitForHarnessRejectedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var data = await WaitForObservationDataAsync(role, "harness-rejected", timeout, additionalDiagnostics);
        return data.GetProperty("content").GetString()!;
    }

    /// <summary>Waits until the connected client has reported the host aborting this role's current operation.</summary>
    public async Task WaitForAbortAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        await WaitForObservationDataAsync(role, "abort", timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported at least the given number of distinct aborts for
    /// the given role - proving a repeated abort produced a genuinely new observation rather than re-matching an
    /// earlier one already reported (unlike <see cref="WaitForAbortAsync"/>, which only ever inspects the
    /// latest).</summary>
    public Task WaitForAbortCountAsync(
        string role, int minimumCount, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForObservationCountAsync(role, "abort", minimumCount, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported at least the given number of observations of the
    /// given kind for the given role.</summary>
    private async Task WaitForObservationCountAsync(
        string role, string kind, int minimumCount, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (myStateLock)
            {
                if (myObservationCountsByRoleAndKind.GetValueOrDefault((role, kind)) >= minimumCount)
                {
                    return;
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report at least {minimumCount} '{kind}' observations across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until the connected client has reported the host cancelling this role's pending
    /// interactions (for example while stopping with a request still outstanding).</summary>
    public async Task WaitForPendingInteractionsCancelledAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        await WaitForObservationDataAsync(role, "pending-interactions-cancelled", timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported a response to a permission request this role's
    /// session emitted, and returns the request id and whether it was approved.</summary>
    public async Task<(string RequestId, bool Approved)> WaitForPermissionResponseAsync(
        string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var data = await WaitForObservationDataAsync(role, "permission-response", timeout, additionalDiagnostics);
        return (data.GetProperty("requestId").GetString()!, data.GetProperty("approved").GetBoolean());
    }

    /// <summary>Waits until the connected client has reported a response to an input request this role's session
    /// emitted, and returns the request id, the answer (or null if none was given), and whether it was
    /// freeform.</summary>
    public async Task<(string RequestId, string? Answer, bool WasFreeform)> WaitForInputResponseAsync(
        string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var data = await WaitForObservationDataAsync(role, "input-response", timeout, additionalDiagnostics);
        return (
            data.GetProperty("requestId").GetString()!,
            data.TryGetProperty("answer", out var answer) && answer.ValueKind != JsonValueKind.Null ? answer.GetString() : null,
            data.GetProperty("wasFreeform").GetBoolean());
    }

    /// <summary>Waits until the connected client has reported a response to an elicitation request this role's
    /// session emitted, and returns the request id, the chosen action, and the accepted content (or null if none
    /// was given).</summary>
    public async Task<(string RequestId, string Action, JsonElement? Content)> WaitForElicitationResponseAsync(
        string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var data = await WaitForObservationDataAsync(role, "elicitation-response", timeout, additionalDiagnostics);
        return (
            data.GetProperty("requestId").GetString()!,
            data.GetProperty("action").GetString()!,
            data.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null ? content : null);
    }

    /// <summary>Returns whether the connected client has reported a response to a permission, input, or
    /// elicitation request this role's session emitted, without waiting - a snapshot read used to prove another
    /// role's owning session never observed a response addressed to a different role.</summary>
    public bool HasObservation(string role, string kind)
    {
        lock (myStateLock)
        {
            return myLatestObservationByRoleAndKind.ContainsKey((role, kind));
        }
    }

    /// <summary>Emits a reasoning update for the given role's session, awaiting the client's acknowledgement that
    /// it published the real production <c>AgentReasoningEvent</c>.</summary>
    public Task EmitReasoningAsync(
        string role, string content, bool isDelta = false, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "reasoning", new { content, isDelta }, timeout, additionalDiagnostics);

    /// <summary>Emits an assistant message update for the given role's session, awaiting the client's
    /// acknowledgement that it published the real production <c>AgentAssistantMessageEvent</c>. Unlike
    /// <see cref="ReplyAsync"/> - which answers an in-flight prompt observed through
    /// <see cref="WaitForPromptAsync(string,TimeSpan?,Func{string}?)"/> - this lets a scenario publish an
    /// assistant message (delta or final) independently of any pending prompt.</summary>
    public Task EmitAssistantAsync(
        string role, string content, bool isDelta = false, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "assistant", new { content, isDelta }, timeout, additionalDiagnostics);

    /// <summary>Emits a system message update for the given role's session, awaiting the client's acknowledgement
    /// that it published the real production <c>AgentSystemMessageEvent</c>.</summary>
    public Task EmitSystemMessageAsync(
        string role, string content, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "system-message", new { content }, timeout, additionalDiagnostics);

    /// <summary>Emits a subagent-started update for the given role's session, awaiting the client's
    /// acknowledgement that it published the real production <c>AgentSubagentStartedEvent</c>.</summary>
    public Task EmitSubagentStartedAsync(
        string role, string? agentName = null, string? agentDisplayName = null, string? model = null,
        TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "subagent-started", new { agentName, agentDisplayName, model }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-started update for the given role's session.</summary>
    public Task EmitToolStartedAsync(
        string role, string toolCallId, string toolName, string? arguments = null, string? toolKind = null,
        string? workingDirectory = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "tool-started", new { toolCallId, toolName, arguments, toolKind, workingDirectory }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-progress update for the given role's session.</summary>
    public Task EmitToolProgressAsync(
        string role, string toolCallId, string progress, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "tool-progress", new { toolCallId, progress }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-output-changed update for the given role's session.</summary>
    public Task EmitToolOutputChangedAsync(
        string role, string toolCallId, string output, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "tool-output-changed", new { toolCallId, output }, timeout, additionalDiagnostics);

    /// <summary>Emits a raw tool partial-output fragment for the given role's session, letting the fake provider's
    /// real production <c>CopilotToolOutputNormalizer</c> - the same one the live Copilot SDK provider uses -
    /// infer cumulative-snapshot versus incremental-delta semantics from the fragment itself, exactly as
    /// production does. Unlike <see cref="EmitToolOutputChangedAsync"/> - which publishes an already-normalized
    /// value verbatim - this proves aggregation, deduplication of repeated content, and rewritten-snapshot
    /// replacement through the real wire protocol.</summary>
    public Task EmitToolPartialOutputAsync(
        string role, string toolCallId, string partialOutput, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "tool-partial-output", new { toolCallId, partialOutput }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-completed update for the given role's session.</summary>
    public Task EmitToolCompletedAsync(
        string role, string toolCallId, string toolName, bool succeeded, string? displayOutputFallback = null,
        string? contentFallback = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(
            role, "tool-completed",
            new { toolCallId, toolName, succeeded, displayOutputFallback, contentFallback },
            timeout, additionalDiagnostics);

    /// <summary>Emits a readiness update for the given role's session.</summary>
    public Task EmitReadinessAsync(
        string role, string state, string? error = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "readiness", new { state, error }, timeout, additionalDiagnostics);

    /// <summary>Emits an AI-credit usage update for the given role's session.</summary>
    public Task EmitUsageAsync(
        string role, decimal aicUsed, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "usage", new { aicUsed }, timeout, additionalDiagnostics);

    /// <summary>Emits a context-token usage update for the given role's session.</summary>
    public Task EmitContextUsageAsync(
        string role, long usedTokens, long limitTokens, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "context-usage", new { usedTokens, limitTokens }, timeout, additionalDiagnostics);

    /// <summary>Emits a permission request for the given role's session.</summary>
    public Task RequestPermissionAsync(
        string role, string requestId, string description, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "permission-request", new { requestId, description }, timeout, additionalDiagnostics);

    /// <summary>Emits an input request for the given role's session.</summary>
    public Task RequestInputAsync(
        string role, string requestId, string prompt, IReadOnlyList<string>? choices = null, bool allowFreeform = true,
        TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "input-request", new { requestId, prompt, choices, allowFreeform }, timeout, additionalDiagnostics);

    /// <summary>Emits an elicitation request for the given role's session.</summary>
    public Task RequestElicitationAsync(
        string role, string requestId, string prompt, string mode, string? url = null, TimeSpan? timeout = null,
        Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "elicitation-request", new { requestId, prompt, mode, url }, timeout, additionalDiagnostics);

    /// <summary>Emits an explicit idle transition for the given role's session.</summary>
    public Task EmitIdleAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "idle", new { }, timeout, additionalDiagnostics);

    /// <summary>Arms the given role's session to reject its very next host-authored harness send with an
    /// exception instead of publishing or reporting it - the only way a scenario can prove that a single failed
    /// notification does not lose durable delivery state or destabilize the host.</summary>
    public Task RejectNextHarnessAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "reject-next-harness", new { }, timeout, additionalDiagnostics);

    /// <summary>Arms the given role's session so its next abort remains pending until explicitly resolved through
    /// <see cref="CompletePendingAbortAsync"/> or <see cref="FailPendingAbortAsync"/> - the deterministic control
    /// a scenario needs to prove abort in-flight behavior (a following prompt waiting for it to finish) without
    /// an arbitrary sleep.</summary>
    public Task ArmPendingAbortAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "arm-pending-abort", new { }, timeout, additionalDiagnostics);

    /// <summary>Resolves the given role's currently pending abort (armed by <see cref="ArmPendingAbortAsync"/>) as
    /// successful.</summary>
    public Task CompletePendingAbortAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "complete-pending-abort", new { }, timeout, additionalDiagnostics);

    /// <summary>Resolves the given role's currently pending abort (armed by <see cref="ArmPendingAbortAsync"/>) as
    /// failed with the given message.</summary>
    public Task FailPendingAbortAsync(
        string role, string message, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "fail-pending-abort", new { message }, timeout, additionalDiagnostics);

    /// <summary>Arms the given role's session so its very next abort fails immediately with the given message
    /// instead of succeeding - proving a failed abort's user-visible outcome without any pending, held-open
    /// state.</summary>
    public Task FailNextAbortAsync(
        string role, string message, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "fail-next-abort", new { message }, timeout, additionalDiagnostics);

    /// <summary>Completes the given role's session gracefully, as production
    /// <see cref="squad.AgentProvider.Abstractions.IAgentSession.Completion"/> resolving successfully.</summary>
    public Task CompleteSessionAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "complete-session", new { }, timeout, additionalDiagnostics);

    /// <summary>Fails the given role's session with the given message, as production
    /// <see cref="squad.AgentProvider.Abstractions.IAgentSession.Completion"/> faulting.</summary>
    public Task FailSessionAsync(
        string role, string message, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "fail-session", new { message }, timeout, additionalDiagnostics);

    /// <summary>
    /// Sends a semantic assistant reply for the given role's session across the pipe and awaits the client's
    /// acknowledgement - or throws a <see cref="FakeProviderControlProtocolException"/> immediately if no session
    /// has ever been observed for that role, or with the client's own explicit diagnostic if the client rejects
    /// the reply (for example because the session has since been disposed), or a
    /// <see cref="FakeProviderControlTimeoutException"/> carrying this server's combined diagnostics if the client
    /// never acknowledges within the given timeout.
    /// </summary>
    public async Task ReplyAsync(string role, string content, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        string sessionId;
        lock (myStateLock)
        {
            if (!myActiveSessionByRole.TryGetValue(role, out sessionId!))
            {
                throw new FakeProviderControlProtocolException(
                    $"No fake-provider session has been observed for role '{role}'.");
            }
        }

        JsonElement response;
        try
        {
            response = await myDuplex!.SendAndAwaitAsync(
                "reply", new { role, sessionId, content }, timeout ?? DefaultTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            throw new FakeProviderControlTimeoutException(
                $"the client to acknowledge a reply for role '{role}'", DescribeDiagnostics(additionalDiagnostics));
        }
        ControlPipeDuplex.EnsureNotProtocolError(response, "reply");
    }

    /// <summary>
    /// Sends one "emit" command of the given kind for the given role's session across the pipe and awaits the
    /// client's acknowledgement that it published the corresponding real production <c>AgentEvent</c> (or
    /// completed/failed the session) - or throws a <see cref="FakeProviderControlProtocolException"/> immediately
    /// if no session has ever been observed for that role, or with the client's own explicit diagnostic if the
    /// client rejects the command, or a <see cref="FakeProviderControlTimeoutException"/> carrying this server's
    /// combined diagnostics if the client never acknowledges within the given timeout.
    /// </summary>
    private async Task EmitAsync(string role, string kind, object data, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        string sessionId;
        lock (myStateLock)
        {
            if (!myActiveSessionByRole.TryGetValue(role, out sessionId!))
            {
                throw new FakeProviderControlProtocolException(
                    $"No fake-provider session has been observed for role '{role}'.");
            }
        }

        JsonElement response;
        try
        {
            response = await myDuplex!.SendAndAwaitAsync(
                "emit", new { role, sessionId, kind, data }, timeout ?? DefaultTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            throw new FakeProviderControlTimeoutException(
                $"the client to acknowledge '{kind}' for role '{role}'", DescribeDiagnostics(additionalDiagnostics));
        }
        ControlPipeDuplex.EnsureNotProtocolError(response, kind);
    }

    /// <summary>Waits until the connected client has reported a generic observation of the given kind for the
    /// given role, and returns its kind-specific data.</summary>
    private async Task<JsonElement> WaitForObservationDataAsync(
        string role, string kind, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (myStateLock)
            {
                if (myLatestObservationByRoleAndKind.TryGetValue((role, kind), out var data))
                {
                    return data.Clone();
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report '{kind}' across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Describes every role whose session was observed to start but never observed to be disposed, or
    /// null if none - so a scenario's emergency teardown can flag a leaked session instead of silently discarding
    /// it.</summary>
    public string? DescribeUndisposedSessions()
    {
        lock (myStateLock)
        {
            var started = myObservations.Where(observation => observation.Type == "session-started")
                .Select(observation => (observation.Role, observation.SessionId));
            var disposed = myObservations.Where(observation => observation.Type == "session-disposed")
                .Select(observation => (observation.Role, observation.SessionId))
                .ToHashSet();
            var leaked = started.Where(session => !disposed.Contains(session)).ToList();
            return leaked.Count == 0
                ? null
                : string.Join('\n', leaked.Select(session => $"role='{session.Role}' session='{session.SessionId}'"));
        }
    }

    private async Task WaitForObservationAsync(string role, string type, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (myStateLock)
            {
                if (myObservations.Any(observation => observation.Role == role && observation.Type == type))
                {
                    return;
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report '{type}' across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>
    /// Builds a diagnostics snapshot of every session-lifecycle observation, latest prompt per role, and
    /// protocol error this server has seen, so a caller beyond this server's own semantic waits (for example
    /// <see cref="BackendScenario"/> combining this with process and UI protocol diagnostics) can report the
    /// same bounded-wait diagnostics without inspecting raw control-pipe traffic by hand.
    /// </summary>
    public string DescribeDiagnostics() => DescribeDiagnostics(additionalDiagnostics: null);

    private string DescribeDiagnostics(Func<string>? additionalDiagnostics)
    {
        lock (myStateLock)
        {
            var observations = myObservations.Count == 0
                ? "(none)"
                : string.Join('\n', myObservations.Select(observation =>
                    $"{observation.Type} role='{observation.Role}' session='{observation.SessionId}'"));
            var prompts = myLatestPromptByRole.Count == 0
                ? "(none)"
                : string.Join('\n', myLatestPromptByRole.Select(entry => $"role='{entry.Key}' prompt='{entry.Value}'"));
            var genericObservations = myLatestObservationByRoleAndKind.Count == 0
                ? "(none)"
                : string.Join('\n', myLatestObservationByRoleAndKind.Select(entry =>
                    $"role='{entry.Key.Role}' kind='{entry.Key.Kind}' data={entry.Value}"));
            var protocolErrors = myProtocolErrors.Count == 0 ? "(none)" : string.Join('\n', myProtocolErrors);
            var provider = $"""
                Observations:
                {observations}
                Latest prompts:
                {prompts}
                Latest generic observations:
                {genericObservations}
                Protocol errors:
                {protocolErrors}
                """;
            return additionalDiagnostics is null ? provider : $"{provider}\n{additionalDiagnostics()}";
        }
    }

    private async Task HandleUnsolicitedAsync(JsonElement envelope, CancellationToken cancellationToken)
    {
        var correlationId = envelope.TryGetProperty("correlationId", out var correlationElement)
            && correlationElement.ValueKind == JsonValueKind.String
            ? correlationElement.GetString()
            : null;

        if (string.IsNullOrEmpty(correlationId))
        {
            await ReplyProtocolErrorAsync(correlationId ?? "", "Missing or empty correlation id.", cancellationToken);
            return;
        }

        if (!envelope.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || versionElement.GetInt32() != ControlPipeDuplex.ProtocolVersion)
        {
            await ReplyProtocolErrorAsync(
                correlationId, $"Unsupported protocol version. Expected {ControlPipeDuplex.ProtocolVersion}.", cancellationToken);
            return;
        }

        var type = envelope.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var payload = envelope.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;

        switch (type)
        {
            case "connect":
                await HandleConnectAsync(correlationId, payload, cancellationToken);
                break;
            case "session-started":
            case "session-disposed":
                await HandleObservationAsync(type, correlationId, payload, cancellationToken);
                break;
            case "prompt":
                await HandlePromptAsync(correlationId, payload, cancellationToken);
                break;
            case "observe":
                await HandleObserveAsync(correlationId, payload, cancellationToken);
                break;
            default:
                await ReplyProtocolErrorAsync(correlationId, $"Unknown command '{type}'.", cancellationToken);
                break;
        }
    }

    private async Task HandleConnectAsync(string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        var token = payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("token", out var tokenElement)
            && tokenElement.ValueKind == JsonValueKind.String
            ? tokenElement.GetString()
            : null;
        if (!string.Equals(token, Token, StringComparison.Ordinal))
        {
            await ReplyProtocolErrorAsync(correlationId, "Invalid authentication token.", cancellationToken);
            return;
        }

        lock (myStateLock)
        {
            myAuthenticated = true;
        }
        await myDuplex!.SendAsync("connected", correlationId, null, cancellationToken);
    }

    private async Task HandleObservationAsync(string type, string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        bool authenticated;
        lock (myStateLock)
        {
            authenticated = myAuthenticated;
        }
        if (!authenticated)
        {
            await ReplyProtocolErrorAsync(correlationId, "The connection has not authenticated.", cancellationToken);
            return;
        }

        var role = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString()
            : null;
        var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(sessionId))
        {
            await ReplyProtocolErrorAsync(correlationId, $"'{type}' requires a role and a session id.", cancellationToken);
            return;
        }

        lock (myStateLock)
        {
            myObservations.Add((role, type, sessionId));
            if (type == "session-started")
            {
                myActiveSessionByRole[role] = sessionId;
            }
        }
        await myDuplex!.SendAsync("ack", correlationId, new { type }, cancellationToken);
    }

    private async Task HandlePromptAsync(string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        bool authenticated;
        lock (myStateLock)
        {
            authenticated = myAuthenticated;
        }
        if (!authenticated)
        {
            await ReplyProtocolErrorAsync(correlationId, "The connection has not authenticated.", cancellationToken);
            return;
        }

        var role = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString()
            : null;
        var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        var prompt = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("prompt", out var promptElement)
            ? promptElement.GetString()
            : null;
        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(sessionId) || prompt is null)
        {
            await ReplyProtocolErrorAsync(correlationId, "'prompt' requires a role, a session id, and prompt text.", cancellationToken);
            return;
        }

        lock (myStateLock)
        {
            myLatestPromptByRole[role] = prompt;
        }
        await myDuplex!.SendAsync("ack", correlationId, new { type = "prompt" }, cancellationToken);
    }

    private async Task HandleObserveAsync(string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        bool authenticated;
        lock (myStateLock)
        {
            authenticated = myAuthenticated;
        }
        if (!authenticated)
        {
            await ReplyProtocolErrorAsync(correlationId, "The connection has not authenticated.", cancellationToken);
            return;
        }

        var role = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString()
            : null;
        var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        var kind = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("kind", out var kindElement)
            ? kindElement.GetString()
            : null;
        var data = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("data", out var dataElement)
            ? dataElement
            : default;
        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(kind))
        {
            await ReplyProtocolErrorAsync(correlationId, "'observe' requires a role, a session id, and a kind.", cancellationToken);
            return;
        }

        lock (myStateLock)
        {
            myLatestObservationByRoleAndKind[(role, kind)] = data.Clone();
            myObservationCountsByRoleAndKind[(role, kind)] = myObservationCountsByRoleAndKind.GetValueOrDefault((role, kind)) + 1;
        }
        await myDuplex!.SendAsync("ack", correlationId, new { type = "observe", kind }, cancellationToken);
    }

    private async Task ReplyProtocolErrorAsync(string correlationId, string message, CancellationToken cancellationToken)
    {
        lock (myStateLock)
        {
            myProtocolErrors.Add(message);
        }
        await myDuplex!.SendAsync("protocol-error", correlationId, new { message }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (myDuplex is not null)
        {
            await myDuplex.DisposeAsync();
        }
        else
        {
            await myPipe.DisposeAsync();
        }
    }
}
