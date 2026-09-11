using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace squad.Specs.Support.Agents.Control;

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
    /// <summary>
    /// Names the environment variable that tells the fake provider runtime to pause immediately before creating
    /// the Nth role's session (0 pauses before the very first session), blocking on the same cancellation token
    /// the production runtime cancels once a host-control shutdown wins its race against startup - so a
    /// specification can prove shutdown requested while provider startup is genuinely paused here still disposes
    /// every session already registered and terminates cleanly. Never read by any production assembly.
    /// </summary>
    public const string StartupGateAfterSessionsEnvironmentVariable = "BLAXQUAD_FAKE_STARTUP_GATE_AFTER_SESSIONS";
    /// <summary>
    /// Names the environment variable that tells the fake provider backend to fail before its runtime becomes
    /// available at all - before any session could possibly be created or connected across the control pipe -
    /// proving squad-hq reports a clean provider diagnostic and terminates without ever reaching readiness even
    /// when the provider fails at the very earliest point production code calls into it. Never read by any
    /// production assembly.
    /// </summary>
    public const string FailBeforeRuntimeEnvironmentVariable = "BLAXQUAD_FAKE_FAIL_BEFORE_RUNTIME";
    /// <summary>
    /// Names the environment variable that tells the fake provider runtime to fail immediately after starting
    /// (and notifying the control pipe about) the given number of sessions, mirroring a real provider whose
    /// runtime throws partway through establishing role sessions - so a specification can prove every session
    /// already started is disposed, every session not yet reached is never started, and squad-hq still reports a
    /// clean diagnostic and terminates rather than crashing with a raw unhandled exception. Never read by any
    /// production assembly.
    /// </summary>
    public const string FailAfterSessionsEnvironmentVariable = "BLAXQUAD_FAKE_FAIL_AFTER_SESSIONS";
    /// <summary>
    /// Names the environment variable that tells the fake provider runtime to fail its own disposal with the
    /// given message, after every session it started has already been disposed (and reported disposed across the
    /// control pipe, when connected) and its control client has already disconnected - mirroring a real provider
    /// runtime whose final retirement step fails even though every individual session it owned was genuinely torn
    /// down first. Set before launch (never through a live control-pipe command), so a specification can pair it
    /// deterministically with any independent primary startup or runtime failure without racing that failure's
    /// own timing against when cleanup actually reaches this runtime. Never read by any production assembly.
    /// </summary>
    public const string FailDisposalMessageEnvironmentVariable = "BLAXQUAD_FAKE_FAIL_DISPOSAL_MESSAGE";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly NamedPipeServerStream myPipe;
    private readonly ObservationJournal myJournal = new();
    private ControlPipeDuplex? myDuplex;
    private volatile bool myAuthenticated;

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
            throw new TimeoutException(
                $"Timed out waiting for a client to connect to the fake-provider control pipe.\n{myJournal.DescribeDiagnostics(additionalDiagnostics)}");
        }
        myDuplex = new ControlPipeDuplex(myPipe, HandleUnsolicitedAsync);
        myDuplex.StartDispatching();
    }

    /// <summary>Waits until the connected client has reported (and this server has acknowledged) that the given
    /// role's session started.</summary>
    public Task WaitForSessionStartedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForLifecycleAsync(role, "session-started", timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported (and this server has acknowledged) that the given
    /// role's session was disposed.</summary>
    public Task WaitForSessionDisposedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForLifecycleAsync(role, "session-disposed", timeout, additionalDiagnostics);

    /// <summary>Whether this server has ever observed a "session-started" notification for the given role - a
    /// snapshot read (no waiting) so a specification can prove a session was never created, not merely that it
    /// has not yet been observed.</summary>
    public bool HasSessionStarted(string role) => myJournal.HasSessionStarted(role);

    /// <summary>Waits until the connected client has reported a prompt sent to the given role, and returns its
    /// content.</summary>
    public Task<string> WaitForPromptAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForPromptAsync(role, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported a prompt sent to the given role whose content
    /// satisfies the given predicate, and returns it. Unlike <see cref="WaitForPromptAsync(string,TimeSpan?,Func{string}?)"/>,
    /// this keeps polling past an already-observed prompt that does not satisfy the predicate (such as an earlier
    /// prompt for the same role, sent before this one was serialized behind it), so a caller can distinguish a
    /// later, distinct prompt from that earlier one.</summary>
    public Task<string> WaitForPromptAsync(
        string role, Func<string, bool> matches, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForPromptAsync(role, matches, timeout, additionalDiagnostics);

    /// <summary>Returns the content of the most recent prompt this role's session has reported across the control
    /// pipe, or null if none has been reported yet - a snapshot read (no waiting) used to prove the absence of a
    /// prompt, or that a role's latest observed prompt has not yet advanced past an earlier one, rather than the
    /// presence of a later one.</summary>
    public string? LatestPrompt(string role) => myJournal.LatestPrompt(role);

    /// <summary>Waits until the connected client has reported the host sending this role's session its initial
    /// harness instruction, and returns its content.</summary>
    public Task<string> WaitForHarnessMessageAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForHarnessMessageAsync(role, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported this role's session receiving a harness message
    /// whose content satisfies the given predicate. Unlike <see cref="WaitForHarnessMessageAsync(string,TimeSpan?,Func{string}?)"/>,
    /// this keeps polling past an already-observed harness message that does not satisfy the predicate (such as
    /// the role's own initial instruction, sent once at session start), so a caller can distinguish a later,
    /// distinct harness message - for example a delivery wake-up - from that earlier one.</summary>
    public Task<string> WaitForHarnessMessageAsync(
        string role, Func<string, bool> matches, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForHarnessMessageAsync(role, matches, timeout, additionalDiagnostics);

    /// <summary>Returns the content of the most recent harness message this role's session has reported across
    /// the control pipe, or null if none has been reported yet - a snapshot read (no waiting) used to prove the
    /// absence of a later harness message rather than the presence of one.</summary>
    public string? LatestHarnessMessage(string role) => myJournal.LatestHarnessMessage(role);

    /// <summary>Waits until the connected client has reported this role's session rejecting a harness send (armed
    /// by a prior test-only "reject next harness" control), and returns its content - proving the host has
    /// observably attempted and failed that send, rather than only that a successful one was never observed.</summary>
    public Task<string> WaitForHarnessRejectedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForHarnessRejectedAsync(role, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported the host aborting this role's current operation.</summary>
    public Task WaitForAbortAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForAbortAsync(role, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported at least the given number of distinct aborts for
    /// the given role - proving a repeated abort produced a genuinely new observation rather than re-matching an
    /// earlier one already reported (unlike <see cref="WaitForAbortAsync"/>, which only ever inspects the
    /// latest).</summary>
    public Task WaitForAbortCountAsync(
        string role, int minimumCount, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForAbortCountAsync(role, minimumCount, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported the host cancelling this role's pending
    /// interactions (for example while stopping with a request still outstanding).</summary>
    public Task WaitForPendingInteractionsCancelledAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForPendingInteractionsCancelledAsync(role, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported a response to a permission request this role's
    /// session emitted, and returns the request id and whether it was approved.</summary>
    public Task<(string RequestId, bool Approved)> WaitForPermissionResponseAsync(
        string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForPermissionResponseAsync(role, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported a response to an input request this role's session
    /// emitted, and returns the request id, the answer (or null if none was given), and whether it was
    /// freeform.</summary>
    public Task<(string RequestId, string? Answer, bool WasFreeform)> WaitForInputResponseAsync(
        string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForInputResponseAsync(role, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported a response to an elicitation request this role's
    /// session emitted, and returns the request id, the chosen action, and the accepted content (or null if none
    /// was given).</summary>
    public Task<(string RequestId, string Action, JsonElement? Content)> WaitForElicitationResponseAsync(
        string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForElicitationResponseAsync(role, timeout, additionalDiagnostics);

    /// <summary>Returns whether the connected client has reported a response to a permission, input, or
    /// elicitation request this role's session emitted, without waiting - a snapshot read used to prove another
    /// role's owning session never observed a response addressed to a different role.</summary>
    public bool HasObservation(string role, string kind) => myJournal.HasObservation(role, kind);

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

    /// <summary>Emits a skill-invoked update for the given role's session, awaiting the client's acknowledgement
    /// that it published the real production <c>AgentSkillInvokedEvent</c>.</summary>
    public Task EmitSkillInvokedAsync(
        string role, string name, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "skill-invoked", new { name }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-started update for the given role's session.</summary>
    public Task EmitToolStartedAsync(
        string role, string toolCallId, string toolName, string? arguments = null, string? toolKind = null,
        string? workingDirectory = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "tool-started", new { toolCallId, toolName, arguments, toolKind, workingDirectory }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-progress update for the given role's session.</summary>
    public Task EmitToolProgressAsync(
        string role, string toolCallId, string progress, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "tool-progress", new { toolCallId, progress }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-output-changed update for the given role's session. The value is already the
    /// complete, provider-computed output for the tool call - the fake provider publishes it verbatim rather than
    /// inferring cumulative-snapshot versus incremental-delta semantics from a raw fragment, since that inference
    /// is a Copilot SDK provider implementation detail with no wire-protocol contract of its own.</summary>
    public Task EmitToolOutputChangedAsync(
        string role, string toolCallId, string output, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "tool-output-changed", new { toolCallId, output }, timeout, additionalDiagnostics);

    /// <summary>Emits a tool-completed update for the given role's session.</summary>
    public Task EmitToolCompletedAsync(
        string role, string toolCallId, string toolName, bool succeeded, string? displayOutputFallback = null,
        string? contentFallback = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(
            role, "tool-completed",
            new { toolCallId, toolName, succeeded, displayOutputFallback, contentFallback },
            timeout, additionalDiagnostics);

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

    /// <summary>Arms the given role's session so every future prompt it receives is answered automatically with
    /// "echo: {prompt}" instead of waiting for an explicit <see cref="ReplyAsync"/> - the semantic operation
    /// real-wire-framing specifications use to drive a genuine transcript update through the real protocol
    /// pipeline without a second, narrower provider fixture.</summary>
    public Task EnableAutoEchoAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "enable-auto-echo", new { }, timeout, additionalDiagnostics);

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

    /// <summary>Arms the given role's session so its disposal, when it happens, first reports a "disposal-held"
    /// observation and then remains pending until <see cref="CompletePendingDisposalAsync"/> resolves it - the
    /// deterministic control a scenario needs to prove backend cleanup genuinely holds at the real provider
    /// boundary without an arbitrary sleep.</summary>
    public Task ArmPendingDisposalAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "arm-pending-disposal", new { }, timeout, additionalDiagnostics);

    /// <summary>Resolves the given role's currently held disposal (armed by <see cref="ArmPendingDisposalAsync"/>),
    /// letting it proceed.</summary>
    public Task CompletePendingDisposalAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        EmitAsync(role, "complete-pending-disposal", new { }, timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported that the given role's session disposal is being
    /// held (armed by <see cref="ArmPendingDisposalAsync"/>), and returns whether an admitted send on this same
    /// session had already reached its own canceled terminal outcome by the moment disposal began - this session's
    /// own in-process record (not an inference from production's own call sequence, and not reliant on any
    /// control-pipe message arrival order) that an admitted prompt's cancellation strictly precedes this same
    /// session's later disposal.</summary>
    public Task<bool> WaitForDisposalHeldAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        myJournal.WaitForDisposalHeldAsync(role, timeout, additionalDiagnostics);

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
    /// Faults the fake provider's whole backend with the given message, as production
    /// <see cref="squad.AgentProvider.Abstractions.IAgentBackendFailureSource.Failure"/> faulting - a fatal,
    /// backend-wide failure independent of any individual role's session, mirroring a real provider whose shared
    /// SDK connection or process dies entirely rather than one role's session failing on its own. Unlike
    /// <see cref="FailSessionAsync"/>, this command names no role or session: it targets the backend itself, so it
    /// can be sent even when no session has ever been observed.
    /// </summary>
    public async Task FailBackendAsync(string message, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        JsonElement response;
        try
        {
            response = await myDuplex!.SendAndAwaitAsync(
                "fail-backend", new { message }, timeout ?? DefaultTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            throw new TimeoutException(
                $"Timed out waiting for the client to acknowledge 'fail-backend'.\n{myJournal.DescribeDiagnostics(additionalDiagnostics)}");
        }
        ControlPipeDuplex.EnsureNotProtocolError(response, "fail-backend");
    }

    /// <summary>
    /// Sends a semantic assistant reply for the given role's session across the pipe and awaits the client's
    /// acknowledgement - or throws an <see cref="InvalidOperationException"/> immediately if no session
    /// has ever been observed for that role, or with the client's own explicit diagnostic if the client rejects
    /// the reply (for example because the session has since been disposed), or a
    /// <see cref="TimeoutException"/> carrying this server's combined diagnostics if the client
    /// never acknowledges within the given timeout.
    /// </summary>
    public async Task ReplyAsync(string role, string content, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        if (!myJournal.TryGetActiveSession(role, out var sessionId))
        {
            throw new InvalidOperationException($"No fake-provider session has been observed for role '{role}'.");
        }

        JsonElement response;
        try
        {
            response = await myDuplex!.SendAndAwaitAsync(
                "reply", new { role, sessionId, content }, timeout ?? DefaultTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            throw new TimeoutException(
                $"Timed out waiting for the client to acknowledge a reply for role '{role}'.\n{myJournal.DescribeDiagnostics(additionalDiagnostics)}");
        }
        ControlPipeDuplex.EnsureNotProtocolError(response, "reply");
    }

    /// <summary>
    /// Sends one "emit" command of the given kind for the given role's session across the pipe and awaits the
    /// client's acknowledgement that it published the corresponding real production <c>AgentEvent</c> (or
    /// completed/failed the session) - or throws an <see cref="InvalidOperationException"/> immediately
    /// if no session has ever been observed for that role, or with the client's own explicit diagnostic if the
    /// client rejects the command, or a <see cref="TimeoutException"/> carrying this server's
    /// combined diagnostics if the client never acknowledges within the given timeout.
    /// </summary>
    private async Task EmitAsync(string role, string kind, object data, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        if (!myJournal.TryGetActiveSession(role, out var sessionId))
        {
            throw new InvalidOperationException($"No fake-provider session has been observed for role '{role}'.");
        }

        JsonElement response;
        try
        {
            response = await myDuplex!.SendAndAwaitAsync(
                "emit", new { role, sessionId, kind, data }, timeout ?? DefaultTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            throw new TimeoutException(
                $"Timed out waiting for the client to acknowledge '{kind}' for role '{role}'.\n{myJournal.DescribeDiagnostics(additionalDiagnostics)}");
        }
        ControlPipeDuplex.EnsureNotProtocolError(response, kind);
    }

    /// <summary>Describes every role whose session was observed to start but never observed to be disposed, or
    /// null if none - so a scenario's emergency teardown can flag a leaked session instead of silently discarding
    /// it.</summary>
    public string? DescribeUndisposedSessions() => myJournal.DescribeUndisposedSessions();

    /// <summary>
    /// Builds a diagnostics snapshot of every session-lifecycle observation, latest prompt per role, and
    /// protocol error this server has seen, so a caller beyond this server's own semantic waits (for example
    /// <see cref="BackendScenario"/> combining this with process and UI protocol diagnostics) can report the
    /// same bounded-wait diagnostics without inspecting raw control-pipe traffic by hand.
    /// </summary>
    public string DescribeDiagnostics() => myJournal.DescribeDiagnostics(additionalDiagnostics: null);
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

        myAuthenticated = true;
        await myDuplex!.SendAsync("connected", correlationId, null, cancellationToken);
    }

    private async Task HandleObservationAsync(string type, string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!myAuthenticated)
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

        myJournal.RecordLifecycle(role, type, sessionId);
        await myDuplex!.SendAsync("ack", correlationId, new { type }, cancellationToken);
    }

    private async Task HandlePromptAsync(string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!myAuthenticated)
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

        myJournal.RecordPrompt(role, prompt);
        await myDuplex!.SendAsync("ack", correlationId, new { type = "prompt" }, cancellationToken);
    }

    private async Task HandleObserveAsync(string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!myAuthenticated)
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

        myJournal.RecordObservation(role, kind, data);
        await myDuplex!.SendAsync("ack", correlationId, new { type = "observe", kind }, cancellationToken);
    }

    private async Task ReplyProtocolErrorAsync(string correlationId, string message, CancellationToken cancellationToken)
    {
        myJournal.RecordProtocolError(message);
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
