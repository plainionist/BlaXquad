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

    /// <summary>Sends a semantic assistant reply for this role's session, awaiting the real production transcript
    /// projection to happen inside the launched process before returning. On timeout, reports one diagnostics
    /// block combining process, UI protocol, and provider/control observations.</summary>
    public Task ReplyAsync(string content, TimeSpan? timeout = null) => control.ReplyAsync(role, content, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported the host sending it its initial harness instruction,
    /// and returns its content.</summary>
    public Task<string> WaitForHarnessMessageAsync(TimeSpan? timeout = null) =>
        control.WaitForHarnessMessageAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported the host aborting its current operation.</summary>
    public Task WaitForAbortAsync(TimeSpan? timeout = null) => control.WaitForAbortAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a response to a permission request it emitted, and
    /// returns the request id and whether it was approved.</summary>
    public Task<(string RequestId, bool Approved)> WaitForPermissionResponseAsync(TimeSpan? timeout = null) =>
        control.WaitForPermissionResponseAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a response to an input request it emitted, and
    /// returns the request id, the answer (or null if none was given), and whether it was freeform.</summary>
    public Task<(string RequestId, string? Answer, bool WasFreeform)> WaitForInputResponseAsync(TimeSpan? timeout = null) =>
        control.WaitForInputResponseAsync(role, timeout, uiDiagnostics);

    /// <summary>Waits until this role's session has reported a response to an elicitation request it emitted, and
    /// returns the request id and the chosen action.</summary>
    public Task<(string RequestId, string Action)> WaitForElicitationResponseAsync(TimeSpan? timeout = null) =>
        control.WaitForElicitationResponseAsync(role, timeout, uiDiagnostics);

    /// <summary>Emits a reasoning update for this role's session, awaiting the real production
    /// <c>AgentReasoningEvent</c> to be published.</summary>
    public Task EmitReasoningAsync(string content, bool isDelta = false, TimeSpan? timeout = null) =>
        control.EmitReasoningAsync(role, content, isDelta, timeout, uiDiagnostics);

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

    /// <summary>Completes this role's session gracefully, as production
    /// <see cref="squad.AgentProvider.Abstractions.IAgentSession.Completion"/> resolving successfully.</summary>
    public Task CompleteSessionAsync(TimeSpan? timeout = null) => control.CompleteSessionAsync(role, timeout, uiDiagnostics);

    /// <summary>Fails this role's session with the given message, as production
    /// <see cref="squad.AgentProvider.Abstractions.IAgentSession.Completion"/> faulting.</summary>
    public Task FailSessionAsync(string message, TimeSpan? timeout = null) =>
        control.FailSessionAsync(role, message, timeout, uiDiagnostics);
}
