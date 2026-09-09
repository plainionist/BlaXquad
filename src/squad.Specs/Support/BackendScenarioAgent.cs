namespace squad.Specs.Support;

/// <summary>
/// Narrow, semantic controller for one role's fake-provider session, returned by <see cref="BackendScenario.Agent"/>.
/// This is the only surface step definitions use to observe prompts a role received and send it semantic assistant
/// replies - every control-pipe DTO and provider <c>AgentEvent</c> value stays behind this API, never leaking into
/// Gherkin step definitions.
/// </summary>
public sealed class BackendScenarioAgent(FakeProviderControlServer control, string role, Func<string>? uiDiagnostics)
{
    /// <summary>Waits until this role's session has reported a prompt across the control pipe and returns its
    /// content. On timeout, reports one diagnostics block combining process, UI protocol, and provider/control
    /// observations.</summary>
    public Task<string> WaitForPromptAsync(TimeSpan? timeout = null) => control.WaitForPromptAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a prompt across the control pipe whose content
    /// satisfies the given predicate, distinguishing it from an already-observed earlier prompt for this role -
    /// for example one still serialized behind an in-flight prompt - by content rather than mere presence.</summary>
    public Task<string> WaitForPromptAsync(Func<string, bool> matches, TimeSpan? timeout = null) =>
        control.WaitForPromptAsync(role, matches, timeout, uiDiagnostics);

    /// <summary>Returns the content of the most recent prompt this role's session has reported across the control
    /// pipe, or null if none has been reported yet - a snapshot read used to prove the absence of a prompt for
    /// this role, or that it has not yet advanced past an earlier one.</summary>
    public string? LatestPrompt() => control.LatestPrompt(role);

    /// <summary>Sends a semantic assistant reply for this role's session, awaiting the real production transcript
    /// projection to happen inside the launched process before returning. On timeout, reports one diagnostics
    /// block combining process, UI protocol, and provider/control observations.</summary>
    public Task ReplyAsync(string content, TimeSpan? timeout = null) => control.ReplyAsync(role, content, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported the host sending it its initial harness instruction,
    /// and returns its content.</summary>
    public Task<string> WaitForHarnessMessageAsync(TimeSpan? timeout = null) =>
        control.WaitForHarnessMessageAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a harness message whose content satisfies the given
    /// predicate, distinguishing it from an already-observed earlier harness message (such as this role's own
    /// initial instruction) by content rather than mere presence.</summary>
    public Task<string> WaitForHarnessMessageAsync(Func<string, bool> matches, TimeSpan? timeout = null) =>
        control.WaitForHarnessMessageAsync(role, matches, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported rejecting a harness send (armed by
    /// <see cref="RejectNextHarnessAsync"/>), and returns its content - proving the host has observably attempted
    /// and failed that send.</summary>
    public Task<string> WaitForHarnessRejectedAsync(TimeSpan? timeout = null) =>
        control.WaitForHarnessRejectedAsync(role, timeout, uiDiagnostics);

    /// <summary>Returns the content of the most recent harness message this role's session has reported, or null
    /// if none has been reported yet - a snapshot read used to prove the absence of a later harness message.</summary>
    public string? LatestHarnessMessage() => control.LatestHarnessMessage(role);

    /// <summary>Waits until this role's session has reported the host aborting its current operation.</summary>
    public Task WaitForAbortAsync(TimeSpan? timeout = null) => control.WaitForAbortAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported at least the given number of distinct aborts -
    /// proving a repeated abort produced a genuinely new observation.</summary>
    public Task WaitForAbortCountAsync(int minimumCount, TimeSpan? timeout = null) =>
        control.WaitForAbortCountAsync(role, minimumCount, timeout, uiDiagnostics);

    /// <summary>Returns whether this role's session has already reported an abort, without waiting - used to
    /// prove an abort addressed to another role never reached this one.</summary>
    public bool HasObservedAbort() => control.HasObservation(role, "abort");

    /// <summary>Arms this role's session so its next abort remains pending until explicitly resolved through
    /// <see cref="CompletePendingAbortAsync"/> or <see cref="FailPendingAbortAsync"/>.</summary>
    public Task ArmPendingAbortAsync(TimeSpan? timeout = null) => control.ArmPendingAbortAsync(role, timeout, uiDiagnostics);

    /// <summary>Resolves this role's currently pending abort (armed by <see cref="ArmPendingAbortAsync"/>) as
    /// successful.</summary>
    public Task CompletePendingAbortAsync(TimeSpan? timeout = null) => control.CompletePendingAbortAsync(role, timeout, uiDiagnostics);

    /// <summary>Resolves this role's currently pending abort (armed by <see cref="ArmPendingAbortAsync"/>) as
    /// failed with the given message.</summary>
    public Task FailPendingAbortAsync(string message, TimeSpan? timeout = null) =>
        control.FailPendingAbortAsync(role, message, timeout, uiDiagnostics);

    /// <summary>Arms this role's session so its very next abort fails immediately with the given message instead
    /// of succeeding.</summary>
    public Task FailNextAbortAsync(string message, TimeSpan? timeout = null) =>
        control.FailNextAbortAsync(role, message, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported the host cancelling its pending interactions (for
    /// example while stopping with a request still outstanding).</summary>
    public Task WaitForPendingInteractionsCancelledAsync(TimeSpan? timeout = null) =>
        control.WaitForPendingInteractionsCancelledAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a response to a permission request it emitted, and
    /// returns the request id and whether it was approved.</summary>
    public Task<(string RequestId, bool Approved)> WaitForPermissionResponseAsync(TimeSpan? timeout = null) =>
        control.WaitForPermissionResponseAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a response to an input request it emitted, and
    /// returns the request id, the answer (or null if none was given), and whether it was freeform.</summary>
    public Task<(string RequestId, string? Answer, bool WasFreeform)> WaitForInputResponseAsync(TimeSpan? timeout = null) =>
        control.WaitForInputResponseAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a response to an elicitation request it emitted, and
    /// returns the request id, the chosen action, and the accepted content (or null if none was given).</summary>
    public Task<(string RequestId, string Action, System.Text.Json.JsonElement? Content)> WaitForElicitationResponseAsync(TimeSpan? timeout = null) =>
        control.WaitForElicitationResponseAsync(role, timeout, uiDiagnostics);

    /// <summary>Returns whether this role's session has already reported a response to a permission request it
    /// emitted, without waiting - used to prove a response addressed to another role never reached this one.</summary>
    public bool HasReceivedPermissionResponse() => control.HasObservation(role, "permission-response");

    /// <summary>Returns whether this role's session has already reported a response to an input request it
    /// emitted, without waiting - used to prove a response addressed to another role never reached this one.</summary>
    public bool HasReceivedInputResponse() => control.HasObservation(role, "input-response");

    /// <summary>Returns whether this role's session has already reported a response to an elicitation request it
    /// emitted, without waiting - used to prove a response addressed to another role never reached this one.</summary>
    public bool HasReceivedElicitationResponse() => control.HasObservation(role, "elicitation-response");

    /// <summary>Emits a reasoning update for this role's session, awaiting the real production
    /// <c>AgentReasoningEvent</c> to be published.</summary>
    public Task EmitReasoningAsync(string content, bool isDelta = false, TimeSpan? timeout = null) =>
        control.EmitReasoningAsync(role, content, isDelta, timeout, uiDiagnostics);

    /// <summary>Emits an assistant message update for this role's session, awaiting the real production
    /// <c>AgentAssistantMessageEvent</c> to be published, independently of any prompt currently awaited through
    /// <see cref="WaitForPromptAsync(TimeSpan?)"/> or answered through <see cref="ReplyAsync"/>.</summary>
    public Task EmitAssistantAsync(string content, bool isDelta = false, TimeSpan? timeout = null) =>
        control.EmitAssistantAsync(role, content, isDelta, timeout, uiDiagnostics);

    /// <summary>Emits a system message update for this role's session, awaiting the real production
    /// <c>AgentSystemMessageEvent</c> to be published.</summary>
    public Task EmitSystemMessageAsync(string content, TimeSpan? timeout = null) =>
        control.EmitSystemMessageAsync(role, content, timeout, uiDiagnostics);

    /// <summary>Emits a subagent-started update for this role's session.</summary>
    public Task EmitSubagentStartedAsync(
        string? agentName = null, string? agentDisplayName = null, string? model = null, TimeSpan? timeout = null) =>
        control.EmitSubagentStartedAsync(role, agentName, agentDisplayName, model, timeout, uiDiagnostics);

    /// <summary>Emits a skill-invoked update for this role's session.</summary>
    public Task EmitSkillInvokedAsync(string name, TimeSpan? timeout = null) =>
        control.EmitSkillInvokedAsync(role, name, timeout, uiDiagnostics);

    /// <summary>Emits a tool-started update for this role's session.</summary>
    public Task EmitToolStartedAsync(
        string toolCallId, string toolName, string? arguments = null, string? toolKind = null,
        string? workingDirectory = null, TimeSpan? timeout = null) =>
        control.EmitToolStartedAsync(role, toolCallId, toolName, arguments, toolKind, workingDirectory, timeout, uiDiagnostics);

    /// <summary>Emits a tool-progress update for this role's session.</summary>
    public Task EmitToolProgressAsync(string toolCallId, string progress, TimeSpan? timeout = null) =>
        control.EmitToolProgressAsync(role, toolCallId, progress, timeout, uiDiagnostics);

    /// <summary>Emits a tool-output-changed update for this role's session.</summary>
    public Task EmitToolOutputChangedAsync(string toolCallId, string output, TimeSpan? timeout = null) =>
        control.EmitToolOutputChangedAsync(role, toolCallId, output, timeout, uiDiagnostics);

    /// <summary>Emits a raw tool partial-output fragment for this role's session, normalized by the real
    /// production <c>CopilotToolOutputNormalizer</c> exactly as the live Copilot SDK provider does.</summary>
    public Task EmitToolPartialOutputAsync(string toolCallId, string partialOutput, TimeSpan? timeout = null) =>
        control.EmitToolPartialOutputAsync(role, toolCallId, partialOutput, timeout, uiDiagnostics);

    /// <summary>Emits a tool-completed update for this role's session.</summary>
    public Task EmitToolCompletedAsync(
        string toolCallId, string toolName, bool succeeded, string? displayOutputFallback = null,
        string? contentFallback = null, TimeSpan? timeout = null) =>
        control.EmitToolCompletedAsync(role, toolCallId, toolName, succeeded, displayOutputFallback, contentFallback, timeout, uiDiagnostics);

    /// <summary>Emits a readiness update for this role's session.</summary>
    public Task EmitReadinessAsync(string state, string? error = null, TimeSpan? timeout = null) =>
        control.EmitReadinessAsync(role, state, error, timeout, uiDiagnostics);

    /// <summary>Emits an AI-credit usage update for this role's session.</summary>
    public Task EmitUsageAsync(decimal aicUsed, TimeSpan? timeout = null) =>
        control.EmitUsageAsync(role, aicUsed, timeout, uiDiagnostics);

    /// <summary>Emits a context-token usage update for this role's session.</summary>
    public Task EmitContextUsageAsync(long usedTokens, long limitTokens, TimeSpan? timeout = null) =>
        control.EmitContextUsageAsync(role, usedTokens, limitTokens, timeout, uiDiagnostics);

    /// <summary>Emits a permission request for this role's session.</summary>
    public Task RequestPermissionAsync(string requestId, string description, TimeSpan? timeout = null) =>
        control.RequestPermissionAsync(role, requestId, description, timeout, uiDiagnostics);

    /// <summary>Emits an input request for this role's session.</summary>
    public Task RequestInputAsync(
        string requestId, string prompt, IReadOnlyList<string>? choices = null, bool allowFreeform = true, TimeSpan? timeout = null) =>
        control.RequestInputAsync(role, requestId, prompt, choices, allowFreeform, timeout, uiDiagnostics);

    /// <summary>Emits an elicitation request for this role's session.</summary>
    public Task RequestElicitationAsync(string requestId, string prompt, string mode, string? url = null, TimeSpan? timeout = null) =>
        control.RequestElicitationAsync(role, requestId, prompt, mode, url, timeout, uiDiagnostics);

    /// <summary>Emits an explicit idle transition for this role's session.</summary>
    public Task EmitIdleAsync(TimeSpan? timeout = null) => control.EmitIdleAsync(role, timeout, uiDiagnostics);

    /// <summary>Arms this role's session to reject its very next host-authored harness send with an exception
    /// instead of publishing or reporting it.</summary>
    public Task RejectNextHarnessAsync(TimeSpan? timeout = null) => control.RejectNextHarnessAsync(role, timeout, uiDiagnostics);

    /// <summary>Completes this role's session gracefully, as production
    /// <see cref="squad.AgentProvider.Abstractions.IAgentSession.Completion"/> resolving successfully.</summary>
    public Task CompleteSessionAsync(TimeSpan? timeout = null) => control.CompleteSessionAsync(role, timeout, uiDiagnostics);

    /// <summary>Fails this role's session with the given message, as production
    /// <see cref="squad.AgentProvider.Abstractions.IAgentSession.Completion"/> faulting.</summary>
    public Task FailSessionAsync(string message, TimeSpan? timeout = null) =>
        control.FailSessionAsync(role, message, timeout, uiDiagnostics);
}
