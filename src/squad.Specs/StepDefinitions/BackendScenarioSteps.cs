using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives one backend-process specification exclusively through <see cref="BackendScenario"/> - the composition
/// root - never touching workspace paths, process handles, protocol DTOs, or product objects directly.
/// </summary>
[Binding]
public sealed class BackendScenarioSteps
{
    private readonly BackendScenario myScenario;
    private int myExitCode;
    private Exception? myLastWaitException;
    private string? myObservedHarnessMessage;
    private int myProtocolErrorsObserved;
    private readonly List<TranscriptUpdateObservation> myObservedTranscriptUpdates = [];

    public BackendScenarioSteps(ScenarioWorkspace workspace)
    {
        myScenario = new BackendScenario(workspace);
    }

    /// <summary>
    /// Disposes the scenario's <see cref="BackendScenario"/> after every scenario - not just the ones that reach a
    /// normal host-control shutdown. This is the teardown path that actually runs for every process-driver
    /// scenario (Reqnroll disposes the injected <see cref="ScenarioWorkspace"/> automatically, but never this
    /// manually constructed composition root), so it is the only place a leaked, never-disposed session can be
    /// reported.
    /// </summary>
    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

    [Given("a backend scenario configured with a {string} role")]
    public void GivenABackendScenarioConfiguredWithARole(string role) => myScenario.ConfigureRole(role);

    [Given("a backend scenario configured with roles {string}")]
    public void GivenABackendScenarioConfiguredWithRoles(string commaSeparatedRoles) =>
        myScenario.ConfigureRoles(commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    [Given("the backend scenario has enabled the fake-provider control transport")]
    public void GivenTheBackendScenarioHasEnabledTheFakeProviderControlTransport() =>
        myScenario.EnableFakeProviderControl();

    [When("the backend scenario starts squad-hq with the echo provider fixture")]
    public void WhenTheBackendScenarioStartsSquadHqWithTheEchoProviderFixture() =>
        Await(myScenario.StartAsync<EchoAgentProviderFactory>());

    [When("the backend scenario starts squad-hq with the fake provider fixture")]
    public void WhenTheBackendScenarioStartsSquadHqWithTheFakeProviderFixture() =>
        Await(myScenario.StartAsync<FakeAgentProviderFactory>());

    [Then("the backend scenario reports the process as ready")]
    public void ThenTheBackendScenarioReportsTheProcessAsReady() =>
        Assert.That(myScenario.IsReady, Is.True);

    [Then("the backend scenario observes role {string} at status {string}")]
    public void ThenTheBackendScenarioObservesRoleAtStatus(string role, string status) =>
        Await(myScenario.WaitForRoleStatusAsync(role, status));

    [Then("the backend scenario observes a session started for role {string} across the control pipe")]
    public void ThenTheBackendScenarioObservesASessionStartedForRoleAcrossTheControlPipe(string role) =>
        Await(myScenario.WaitForRoleSessionStartedAsync(role));

    [Then("the backend scenario observes a session disposed for role {string} across the control pipe")]
    public void ThenTheBackendScenarioObservesASessionDisposedForRoleAcrossTheControlPipe(string role) =>
        Await(myScenario.WaitForRoleSessionDisposedAsync(role));

    [When("the backend scenario sends the prompt {string} to role {string}")]
    public void WhenTheBackendScenarioSendsThePromptToRole(string prompt, string role) =>
        myScenario.SendPrompt(role, prompt);

    [Then("the {string} agent observes the prompt {string}")]
    public void ThenTheAgentObservesThePrompt(string role, string expectedPrompt) =>
        // Matches on content (not just "any latest prompt") so this keeps polling past an earlier, already-observed
        // prompt for the same role (e.g. one sent before an abort) rather than returning it stale.
        Assert.That(Await(myScenario.Agent(role).WaitForPromptAsync(prompt => prompt == expectedPrompt)), Is.EqualTo(expectedPrompt));

    [When("the {string} agent replies with {string}")]
    public void WhenTheAgentRepliesWith(string role, string content) =>
        Await(myScenario.Agent(role).ReplyAsync(content));

    [Then("the backend scenario observes the transcript for role {string} containing {string}")]
    public void ThenTheBackendScenarioObservesTheTranscriptForRoleContaining(string role, string content) =>
        Await(myScenario.WaitForTranscriptAsync(role, content));

    [Then("the {string} agent observes a harness message")]
    public void ThenTheAgentObservesAHarnessMessage(string role) =>
        myObservedHarnessMessage = Await(myScenario.Agent(role).WaitForHarnessMessageAsync());

    [Then("the observed harness message appears in the transcript for role {string}")]
    public void ThenTheObservedHarnessMessageAppearsInTheTranscriptForRole(string role)
    {
        Assert.That(myObservedHarnessMessage, Is.Not.Null);
        Await(myScenario.WaitForTranscriptAsync(role, myObservedHarnessMessage!));
    }

    [When("the backend scenario requests an abort for role {string}")]
    public void WhenTheBackendScenarioRequestsAnAbortForRole(string role) => myScenario.RequestAbort(role);

    [Then("the {string} agent observes an abort")]
    public void ThenTheAgentObservesAnAbort(string role) => Await(myScenario.Agent(role).WaitForAbortAsync());

    [Then("the {string} agent has not observed an abort")]
    public void ThenTheAgentHasNotObservedAnAbort(string role) => Assert.That(myScenario.Agent(role).HasObservedAbort(), Is.False);

    [Then("the {string} agent observes {int} aborts")]
    public void ThenTheAgentObservesAborts(string role, int count) => Await(myScenario.Agent(role).WaitForAbortCountAsync(count));

    [Then("the {string} agent has only observed the prompt {string}")]
    public void ThenTheAgentHasOnlyObservedThePrompt(string role, string expectedPrompt) =>
        Assert.That(myScenario.Agent(role).LatestPrompt(), Is.EqualTo(expectedPrompt));

    [Then("the {string} agent has not received the prompt {string} within {int} seconds")]
    public void ThenTheAgentHasNotReceivedThePromptWithinSeconds(string role, string prompt, int seconds) =>
        Assert.CatchAsync<TimeoutException>(
            () => myScenario.Agent(role).WaitForPromptAsync(observed => observed == prompt, TimeSpan.FromSeconds(seconds)));

    [When("the backend scenario arms role {string} to hold its next abort pending")]
    public void WhenTheBackendScenarioArmsRoleToHoldItsNextAbortPending(string role) =>
        Await(myScenario.Agent(role).ArmPendingAbortAsync());

    [When("the backend scenario completes the pending abort for role {string}")]
    public void WhenTheBackendScenarioCompletesThePendingAbortForRole(string role) =>
        Await(myScenario.Agent(role).CompletePendingAbortAsync());

    [When("the backend scenario arms role {string} to fail its next abort with message {string}")]
    public void WhenTheBackendScenarioArmsRoleToFailItsNextAbortWithMessage(string role, string message) =>
        Await(myScenario.Agent(role).FailNextAbortAsync(message));

    [Then("role {string} is not ready for a prompt")]
    public void ThenRoleIsNotReadyForAPrompt(string role)
    {
        // A short, independently bounded probe against the same live host proves the role is genuinely not ready
        // yet: it polls the host for its own full timeout before concluding "not ready", so its completion is
        // evidence of a live, contacted host currently reporting this role as not ready - not a guess about how
        // long a fixed sleep should be.
        var probe = myScenario.StartWaitForAgent(role, TimeSpan.FromSeconds(1));
        var probeResult = Await(probe.WaitForCompletionAsync(TimeSpan.FromSeconds(5)));
        Assert.That(probeResult.StdErr, Does.Contain("agent not ready"), () => probeResult.StdErr);
    }

    [Then("the backend scenario observes no pending permission {string} for role {string}")]
    public void ThenTheBackendScenarioObservesNoPendingPermissionForRole(string requestId, string role) =>
        Await(myScenario.WaitForNoPendingPermissionAsync(role, requestId));

    [Then("the backend scenario does not observe the transcript for role {string} containing {string} within {int} seconds")]
    public void ThenTheBackendScenarioDoesNotObserveTheTranscriptForRoleContainingWithinSeconds(string role, string content, int seconds) =>
        Assert.CatchAsync<TimeoutException>(
            () => myScenario.WaitForTranscriptAsync(role, content, TimeSpan.FromSeconds(seconds)));

    [Then("the {string} agent observes its pending interactions were cancelled")]
    public void ThenTheAgentObservesItsPendingInteractionsWereCancelled(string role) =>
        Await(myScenario.Agent(role).WaitForPendingInteractionsCancelledAsync());

    [When("the {string} agent requests permission {string} with description {string}")]
    public void WhenTheAgentRequestsPermissionWithDescription(string role, string requestId, string description) =>
        Await(myScenario.Agent(role).RequestPermissionAsync(requestId, description));

    [When("the backend scenario responds to permission {string} for role {string} with approved {string}")]
    public void WhenTheBackendScenarioRespondsToPermissionForRoleWithApproved(string requestId, string role, string approved) =>
        myScenario.RespondToPermission(role, requestId, bool.Parse(approved));

    [Then("the {string} agent observes a permission response for {string} approved {string}")]
    public void ThenTheAgentObservesAPermissionResponseForApproved(string role, string requestId, string approved)
    {
        var response = Await(myScenario.Agent(role).WaitForPermissionResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Approved, Is.EqualTo(bool.Parse(approved)));
        });
    }

    [When("the {string} agent requests input {string} with prompt {string}")]
    public void WhenTheAgentRequestsInputWithPrompt(string role, string requestId, string prompt) =>
        Await(myScenario.Agent(role).RequestInputAsync(requestId, prompt));

    [When("the {string} agent requests input {string} with prompt {string} and choices {string} and freeform {string}")]
    public void WhenTheAgentRequestsInputWithPromptAndChoicesAndFreeform(
        string role, string requestId, string prompt, string commaSeparatedChoices, string allowFreeform) =>
        Await(myScenario.Agent(role).RequestInputAsync(requestId, prompt, ParseChoices(commaSeparatedChoices), bool.Parse(allowFreeform)));

    [When("the backend scenario responds to input {string} for role {string} with answer {string}")]
    public void WhenTheBackendScenarioRespondsToInputForRoleWithAnswer(string requestId, string role, string answer) =>
        myScenario.RespondToInput(role, requestId, answer);

    [Then("the {string} agent observes an input response for {string} with answer {string}")]
    public void ThenTheAgentObservesAnInputResponseForWithAnswer(string role, string requestId, string answer)
    {
        var response = Await(myScenario.Agent(role).WaitForInputResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Answer, Is.EqualTo(answer));
        });
    }

    [When("the {string} agent requests elicitation {string} with prompt {string} and mode {string}")]
    public void WhenTheAgentRequestsElicitationWithPromptAndMode(string role, string requestId, string prompt, string mode) =>
        Await(myScenario.Agent(role).RequestElicitationAsync(requestId, prompt, mode));

    [When("the {string} agent requests URL elicitation {string} with prompt {string} and url {string}")]
    public void WhenTheAgentRequestsUrlElicitationWithPromptAndUrl(string role, string requestId, string prompt, string url) =>
        Await(myScenario.Agent(role).RequestElicitationAsync(requestId, prompt, "url", url));

    [When("the backend scenario responds to elicitation {string} for role {string} with action {string}")]
    public void WhenTheBackendScenarioRespondsToElicitationForRoleWithAction(string requestId, string role, string action) =>
        myScenario.RespondToElicitation(role, requestId, action);

    [When("the backend scenario responds to elicitation {string} for role {string} with action {string} and form value {string}")]
    public void WhenTheBackendScenarioRespondsToElicitationForRoleWithActionAndFormValue(
        string requestId, string role, string action, string formValue) =>
        myScenario.RespondToElicitation(role, requestId, action, new { answer = formValue });

    [Then("the {string} agent observes an elicitation response for {string} with action {string}")]
    public void ThenTheAgentObservesAnElicitationResponseForWithAction(string role, string requestId, string action)
    {
        var response = Await(myScenario.Agent(role).WaitForElicitationResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Action, Is.EqualTo(action));
        });
    }

    [Then("the {string} agent observes an elicitation response for {string} with action {string} and form value {string}")]
    public void ThenTheAgentObservesAnElicitationResponseForWithActionAndFormValue(
        string role, string requestId, string action, string formValue)
    {
        var response = Await(myScenario.Agent(role).WaitForElicitationResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Action, Is.EqualTo(action));
            Assert.That(response.Content?.GetProperty("answer").GetString(), Is.EqualTo(formValue));
        });
    }

    [Then("the {string} agent has not observed a permission response")]
    public void ThenTheAgentHasNotObservedAPermissionResponse(string role) =>
        Assert.That(myScenario.Agent(role).HasReceivedPermissionResponse(), Is.False);

    [Then("the {string} agent has not observed an input response")]
    public void ThenTheAgentHasNotObservedAnInputResponse(string role) =>
        Assert.That(myScenario.Agent(role).HasReceivedInputResponse(), Is.False);

    [Then("the {string} agent has not observed an elicitation response")]
    public void ThenTheAgentHasNotObservedAnElicitationResponse(string role) =>
        Assert.That(myScenario.Agent(role).HasReceivedElicitationResponse(), Is.False);

    [Then("the backend scenario observes a pending permission {string} for role {string} with description {string}")]
    public void ThenTheBackendScenarioObservesAPendingPermissionForRoleWithDescription(string requestId, string role, string description) =>
        Await(myScenario.WaitForPendingPermissionAsync(role, requestId, description));

    [Then("the backend scenario observes a pending input {string} for role {string} with prompt {string} and choices {string} and freeform {string}")]
    public void ThenTheBackendScenarioObservesAPendingInputForRoleWithPromptAndChoicesAndFreeform(
        string requestId, string role, string prompt, string commaSeparatedChoices, string allowFreeform) =>
        Await(myScenario.WaitForPendingInputAsync(role, requestId, prompt, ParseChoices(commaSeparatedChoices), bool.Parse(allowFreeform)));

    [Then("the backend scenario observes a pending elicitation {string} for role {string} with prompt {string} and mode {string}")]
    public void ThenTheBackendScenarioObservesAPendingElicitationForRoleWithPromptAndMode(
        string requestId, string role, string prompt, string mode) =>
        Await(myScenario.WaitForPendingElicitationAsync(role, requestId, prompt, mode));

    [Then("the backend scenario observes a pending elicitation {string} for role {string} with prompt {string} and mode {string} and url {string}")]
    public void ThenTheBackendScenarioObservesAPendingElicitationForRoleWithPromptAndModeAndUrl(
        string requestId, string role, string prompt, string mode, string url) =>
        Await(myScenario.WaitForPendingElicitationAsync(role, requestId, prompt, mode, url));

    [Then("the backend scenario observes a protocol error mentioning {string}")]
    public void ThenTheBackendScenarioObservesAProtocolErrorMentioning(string text)
    {
        var message = Await(myScenario.WaitForProtocolErrorAsync(skip: myProtocolErrorsObserved));
        myProtocolErrorsObserved++;
        Assert.That(message, Does.Contain(text));
    }


    [When("the {string} agent emits the reasoning {string}")]
    public void WhenTheAgentEmitsTheReasoning(string role, string content) =>
        Await(myScenario.Agent(role).EmitReasoningAsync(content));

    [When("the {string} agent emits a reasoning delta {string}")]
    public void WhenTheAgentEmitsAReasoningDelta(string role, string content) =>
        Await(myScenario.Agent(role).EmitReasoningAsync(content, isDelta: true));

    [When("the {string} agent emits a final reasoning message {string}")]
    public void WhenTheAgentEmitsAFinalReasoningMessage(string role, string content) =>
        Await(myScenario.Agent(role).EmitReasoningAsync(content, isDelta: false));

    [When("the {string} agent emits an assistant delta {string}")]
    public void WhenTheAgentEmitsAnAssistantDelta(string role, string content) =>
        Await(myScenario.Agent(role).EmitAssistantAsync(content, isDelta: true));

    [When("the {string} agent emits a final assistant message {string}")]
    public void WhenTheAgentEmitsAFinalAssistantMessage(string role, string content) =>
        Await(myScenario.Agent(role).EmitAssistantAsync(content, isDelta: false));

    [When("the {string} agent emits a system message {string}")]
    public void WhenTheAgentEmitsASystemMessage(string role, string content) =>
        Await(myScenario.Agent(role).EmitSystemMessageAsync(content));

    [Then("the backend scenario observes a transcript update for role {string} with source {string}")]
    public void ThenTheBackendScenarioObservesATranscriptUpdateForRoleWithSource(string role, string source) =>
        myObservedTranscriptUpdates.Add(Await(myScenario.WaitForTranscriptUpdateAsync(role, source)));

    [Then("the backend scenario observes a transcript update for role {string} with source {string} and content {string}")]
    public void ThenTheBackendScenarioObservesATranscriptUpdateForRoleWithSourceAndContent(string role, string source, string content) =>
        myObservedTranscriptUpdates.Add(Await(myScenario.WaitForTranscriptUpdateAsync(role, source, content)));

    [Then("the backend scenario observes a transcript update for role {string} with operation {string} and content {string}")]
    public void ThenTheBackendScenarioObservesATranscriptUpdateForRoleWithOperationAndContent(string role, string operation, string content) =>
        myObservedTranscriptUpdates.Add(Await(myScenario.WaitForTranscriptUpdateByOperationAsync(role, operation, content)));

    [Then("the most recently observed transcript updates for role {string} report the same entry index")]
    public void ThenTheMostRecentlyObservedTranscriptUpdatesForRoleReportTheSameEntryIndex(string role)
    {
        var (previous, current) = TwoMostRecentlyObservedTranscriptUpdates(role);
        Assert.That(current.EntryIndex, Is.EqualTo(previous.EntryIndex));
    }

    [Then("the most recently observed transcript updates for role {string} report different entry indices")]
    public void ThenTheMostRecentlyObservedTranscriptUpdatesForRoleReportDifferentEntryIndices(string role)
    {
        var (previous, current) = TwoMostRecentlyObservedTranscriptUpdates(role);
        Assert.That(current.EntryIndex, Is.Not.EqualTo(previous.EntryIndex));
    }

    private (TranscriptUpdateObservation Previous, TranscriptUpdateObservation Current) TwoMostRecentlyObservedTranscriptUpdates(string role)
    {
        var updatesForRole = myObservedTranscriptUpdates.Where(update => update.Role == role).ToList();
        Assert.That(updatesForRole, Has.Count.GreaterThanOrEqualTo(2));
        return (updatesForRole[^2], updatesForRole[^1]);
    }

    [Then("every observed transcript update for role {string} reports a strictly increasing sequence and entry index")]
    public void ThenEveryObservedTranscriptUpdateForRoleReportsAStrictlyIncreasingSequenceAndEntryIndex(string role)
    {
        var updatesForRole = myObservedTranscriptUpdates.Where(update => update.Role == role).ToList();
        Assert.That(updatesForRole, Has.Count.GreaterThan(1));
        for (var index = 1; index < updatesForRole.Count; index++)
        {
            var previous = updatesForRole[index - 1];
            var current = updatesForRole[index];
            Assert.Multiple(() =>
            {
                Assert.That(current.Sequence, Is.GreaterThan(previous.Sequence));
                Assert.That(current.EntryIndex, Is.GreaterThan(previous.EntryIndex));
                Assert.That(current.Operation, Is.EqualTo("append"));
            });
        }
    }

    [When("the backend scenario requests a fresh transcript synchronization")]
    public void WhenTheBackendScenarioRequestsAFreshTranscriptSynchronization() =>
        myScenario.RequestTranscriptSynchronization();

    [Then("the transcript synchronization for role {string} reports every supported entry source with dashboard protocol fields:")]
    public void ThenTheTranscriptSynchronizationForRoleReportsEverySupportedEntrySourceWithDashboardProtocolFields(string role, Table expected)
    {
        var expectedEntries = expected.Rows.Select(row => (Source: row["source"], Content: row["content"])).ToList();

        // A single synchronization message must carry every expected source - independent first-match waits could
        // otherwise each be satisfied by a different message, including the handshake synchronize that "ui.ready"
        // always publishes before this scenario's later sources exist.
        var synchronization = Await(myScenario.WaitForTranscriptSynchronizationAsync(
            role,
            entries => expectedEntries.All(expectedEntry =>
                entries.Any(entry => entry.Source == expectedEntry.Source && entry.Content == expectedEntry.Content))));

        Assert.That(synchronization.Sequence, Is.GreaterThan(0), "The synchronization message must report a real sequence.");

        var entryIndices = synchronization.Entries.Select(entry => entry.EntryIndex).ToList();
        for (var index = 1; index < entryIndices.Count; index++)
        {
            Assert.That(entryIndices[index], Is.GreaterThan(entryIndices[index - 1]),
                "Synchronization entries must report strictly increasing entry indices.");
        }
    }

    [Then("the transcript synchronization for role {string} includes an entry with source {string} and content {string}")]
    public void ThenTheTranscriptSynchronizationForRoleIncludesAnEntryWithSourceAndContent(string role, string source, string content) =>
        Await(myScenario.WaitForTranscriptSynchronizationAsync(
            role, entries => entries.Any(entry => entry.Source == source && entry.Content == content)));

    [Then("the transcript synchronization for role {string} includes exactly these entries:")]
    public void ThenTheTranscriptSynchronizationForRoleIncludesExactlyTheseEntries(string role, Table expected)
    {
        var expectedEntries = expected.Rows.Select(row => (Source: row["source"], Content: row["content"])).ToList();
        var expectedSources = expectedEntries.Select(entry => entry.Source).ToHashSet();

        // Filtering the observed entries to the expected sources before comparing (rather than requiring an exact
        // match across the whole snapshot) ignores unrelated automatic entries - such as the harness "Session
        // started." entry every session publishes - while still proving no extra or missing entry exists among
        // the sources this scenario cares about (for example a leftover streamed draft alongside its final
        // replacement).
        var synchronization = Await(myScenario.WaitForTranscriptSynchronizationAsync(
            role,
            entries => entries.Where(entry => expectedSources.Contains(entry.Source))
                .Select(entry => (entry.Source, entry.Content))
                .SequenceEqual(expectedEntries)));

        var actualEntries = synchronization.Entries
            .Where(entry => expectedSources.Contains(entry.Source))
            .Select(entry => (entry.Source, entry.Content))
            .ToList();
        Assert.That(actualEntries, Is.EqualTo(expectedEntries));
    }

    [When("the {string} agent emits a full tool lifecycle for tool call {string} named {string}")]
    public void WhenTheAgentEmitsAFullToolLifecycleForToolCallNamed(string role, string toolCallId, string toolName) =>
        Await(EmitFullToolLifecycleAsync(role, toolCallId, toolName));

    [When("the {string} agent emits idle")]
    public void WhenTheAgentEmitsIdle(string role) => Await(myScenario.Agent(role).EmitIdleAsync());

    [When("the {string} agent emits readiness {string}")]
    public void WhenTheAgentEmitsReadiness(string role, string state) => Await(myScenario.Agent(role).EmitReadinessAsync(state));

    [When("the {string} agent emits usage {string}")]
    public void WhenTheAgentEmitsUsage(string role, string aicUsed) =>
        Await(myScenario.Agent(role).EmitUsageAsync(decimal.Parse(aicUsed)));

    [Then("the backend scenario observes role {string} at AI-credit usage {string}")]
    public void ThenTheBackendScenarioObservesRoleAtAiCreditUsage(string role, string aicUsed) =>
        Await(myScenario.WaitForRoleUsageAsync(role, decimal.Parse(aicUsed)));

    [When("the {string} agent completes its session")]
    public void WhenTheAgentCompletesItsSession(string role) => Await(myScenario.Agent(role).CompleteSessionAsync());

    [When("the {string} agent fails its session with message {string}")]
    public void WhenTheAgentFailsItsSessionWithMessage(string role, string message) =>
        Await(myScenario.Agent(role).FailSessionAsync(message));

    private async Task EmitFullToolLifecycleAsync(string role, string toolCallId, string toolName)
    {
        var agent = myScenario.Agent(role);
        await agent.EmitToolStartedAsync(toolCallId, toolName);
        await agent.EmitToolProgressAsync(toolCallId, "Running...");
        await agent.EmitToolOutputChangedAsync(toolCallId, "partial output");
        await agent.EmitToolCompletedAsync(toolCallId, toolName, succeeded: true);
    }

    [When("the backend scenario requests a host-control shutdown")]
    public void WhenTheBackendScenarioRequestsAHostControlShutdown() =>
        myExitCode = Await(myScenario.ShutdownAsync());

    [Then("the backend scenario observes an exit code of zero")]
    public void ThenTheBackendScenarioObservesAnExitCodeOfZero() =>
        Assert.That(myExitCode, Is.Zero);

    [When("the backend scenario waits {int} seconds for role {string} at status {string}")]
    public void WhenTheBackendScenarioWaitsSecondsForRoleAtStatus(int seconds, string role, string status) =>
        Await(WaitAndCaptureAsync(role, status, TimeSpan.FromSeconds(seconds)));

    [Then("the wait fails with a diagnostics block naming the process, the UI protocol state, and the provider observations")]
    public void ThenTheWaitFailsWithCombinedDiagnostics()
    {
        Assert.That(myLastWaitException, Is.Not.Null);
        var message = myLastWaitException!.Message;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Process:"));
            Assert.That(message, Does.Contain("Last known UI state:"));
            Assert.That(message, Does.Contain("Observations:"));
        });
    }

    private async Task WaitAndCaptureAsync(string role, string status, TimeSpan timeout)
    {
        myLastWaitException = null;
        try
        {
            await myScenario.WaitForRoleStatusAsync(role, status, timeout);
        }
        catch (Exception exception)
        {
            myLastWaitException = exception;
        }
    }

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();

    /// <summary>Splits a comma-separated choices column into a list, or null for an empty column - representing
    /// an input request published or observed without any choices at all, rather than an empty choices list.</summary>
    private static IReadOnlyList<string>? ParseChoices(string commaSeparatedChoices) =>
        commaSeparatedChoices.Length == 0 ? null : commaSeparatedChoices.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
