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
}
