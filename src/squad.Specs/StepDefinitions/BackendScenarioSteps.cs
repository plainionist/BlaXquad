using System.Text.Json;
using squad.AgentProvider.Fake;
using squad.Domain;
using squad.Specs.Support.Scenarios;
using squad.Specs.Support.Ui;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives one backend-process specification exclusively through <see cref="BackendScenario"/> - the composition
/// root - never touching workspace paths, process handles, protocol DTOs, or product objects directly.
/// </summary>
[Binding]
public sealed class BackendScenarioSteps
{
    private readonly BackendScenario myScenario;
    private string? myObservedHarnessMessage;
    private int? myObservedExitCode;
    private int myProtocolErrorsObserved;
    private readonly Dictionary<SquadMemberId, MemberTranscriptObservationState> myTranscriptObservations = [];
    private ArchivedTranscriptEntryObservation? myLatestArchivedEntry;
    private RejectedCommandEffect? myLastRejectedCommandEffect;

    public BackendScenarioSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    /// <summary>
    /// Disposes the scenario's shared <see cref="BackendScenario"/> after every scenario - not just the ones that
    /// reach a normal Headquarters-control shutdown. This explicit call must run before <see cref="ScenarioWorkspace"/>'s
    /// own disposal (which Reqnroll's container also triggers automatically, and which forcibly disposes every
    /// process it tracked, including this one's), so <see cref="BackendScenario.Dispose"/> can still request a
    /// clean shutdown and inspect real process state. Reqnroll's container disposes this same constructor-injected
    /// instance a second time as its resolved owner; that second call is a safe no-op.
    /// </summary>
    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

    [Given("a backend scenario configured with a {string} role")]
    public void GivenABackendScenarioConfiguredWithARole(string role) => myScenario.ConfigureRole(role);

    [Given("a backend scenario configured with roles {string}")]
    public void GivenABackendScenarioConfiguredWithRoles(string commaSeparatedRoles) =>
        myScenario.ConfigureRoles(commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    [Given("a backend scenario configured with role {string} shared by members {string}")]
    public void GivenABackendScenarioConfiguredWithRoleSharedByMembers(string role, string commaSeparatedMembers) =>
        myScenario.ConfigureRoleSharedByMembers(
            role,
            commaSeparatedMembers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    [Given("a backend scenario configured with roles {string} and the raw configuration:")]
    public void GivenABackendScenarioConfiguredWithRolesAndTheRawConfiguration(string commaSeparatedRoles, string rawConfiguration) =>
        myScenario.ConfigureProjectWithRawConfiguration(
            rawConfiguration,
            commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    [Given("a backend scenario configured with the raw configuration:")]
    public void GivenABackendScenarioConfiguredWithTheRawConfiguration(string rawConfiguration) =>
        myScenario.ConfigureProjectWithRawConfiguration(rawConfiguration);

    [Given("a backend scenario configured with roles {string} and no leader")]
    public void GivenABackendScenarioConfiguredWithRolesAndNoLeader(string commaSeparatedRoles)
    {
        var roles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myScenario.ConfigureRoles(roles);
        myScenario.SetLeader(null, roles);
    }

    [Given("a backend scenario configured with roles {string} and leader {string}")]
    public void GivenABackendScenarioConfiguredWithRolesAndLeader(string commaSeparatedRoles, string leader)
    {
        var roles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myScenario.ConfigureRoles(roles);
        myScenario.SetLeader(leader, roles);
    }

    [Given("the backend scenario has enabled the fake-provider control transport")]
    public void GivenTheBackendScenarioHasEnabledTheFakeProviderControlTransport() =>
        myScenario.EnableFakeProviderControl();

    [Given("the backend scenario gates provider startup after {int} session has started")]
    public void GivenTheBackendScenarioGatesProviderStartupAfterSessionHasStarted(int count) =>
        myScenario.GateProviderStartupAfterSessions(count);

    [Given("the backend scenario configures the fake provider to fail before its runtime becomes available")]
    public void GivenTheBackendScenarioConfiguresTheFakeProviderToFailBeforeItsRuntimeBecomesAvailable() =>
        myScenario.FailProviderBeforeRuntime();

    [Given("the backend scenario configures the fake provider to fail after {int} session has started")]
    public void GivenTheBackendScenarioConfiguresTheFakeProviderToFailAfterSessionHasStarted(int count) =>
        myScenario.FailProviderAfterSessions(count);

    [Given("the backend scenario configures the fake provider to fail its cleanup with message {string}")]
    public void GivenTheBackendScenarioConfiguresTheFakeProviderToFailItsCleanupWithMessage(string message) =>
        myScenario.FailProviderDisposal(message);

    [Given("the backend scenario isolates its temporary transcript directory")]
    public void GivenTheBackendScenarioIsolatesItsTemporaryTranscriptDirectory() =>
        myScenario.IsolateTemporaryDirectory();

    [Given("Headquarters' temporary transcript directory is isolated")]
    public void GivenHeadquartersTemporaryTranscriptDirectoryIsIsolated() =>
        myScenario.IsolateTemporaryDirectory();

    [Then("Headquarters' temporary transcript history exists")]
    public void ThenHeadquartersTemporaryTranscriptHistoryExists() =>
        Assert.That(myScenario.HasTemporaryTranscriptHistory(), Is.True);

    [Then("Headquarters' temporary transcript history no longer exists")]
    public void ThenHeadquartersTemporaryTranscriptHistoryNoLongerExists() =>
        Assert.That(myScenario.HasTemporaryTranscriptHistory(), Is.False);

    [When("the backend scenario starts squad-hq with the fake provider fixture")]
    public void WhenTheBackendScenarioStartsSquadHqWithTheFakeProviderFixture() =>
        Await(myScenario.StartAsync<FakeAgentProviderFactory>());

    // Deliberately used for a configuration invalid enough that the process exits before ever completing the
    // "ui.ready" handshake: StartAsync's own explicit "--hosting" descriptor keeps this reliable, unlike
    // LaunchWithoutReadyHandshake's "--ui stdio" launch (which never selects a hosting plug-in and is unrelated to
    // this specification). Catches the resulting handshake timeout here - not with the scenario's own generic
    // failure handling - so a later step can still inspect the process's own exit code and captured standard
    // error precisely, matching the diagnostic the configuration loader actually reported.
    [When("the backend scenario attempts to start squad-hq with the fake provider fixture")]
    public void WhenTheBackendScenarioAttemptsToStartSquadHqWithTheFakeProviderFixture() =>
        Assert.CatchAsync(() => myScenario.StartAsync<FakeAgentProviderFactory>(TimeSpan.FromSeconds(5)));

    [When("the backend scenario launches squad-hq with the fake provider fixture without completing the ready handshake")]
    public void WhenTheBackendScenarioLaunchesSquadHqWithTheFakeProviderFixtureWithoutCompletingTheReadyHandshake() =>
        myScenario.LaunchWithoutReadyHandshake<FakeAgentProviderFactory>();

    [When("the backend scenario waits for its rejected startup process to exit")]
    public void WhenTheBackendScenarioWaitsForItsRejectedStartupProcessToExit() =>
        myObservedExitCode = Await(myScenario.WaitForProcessExitAsync());

    [Then("the backend scenario observes its rejected startup exited with a non-zero code")]
    public void ThenTheBackendScenarioObservesItsRejectedStartupExitedWithANonZeroCode() =>
        Assert.That(myObservedExitCode, Is.Not.EqualTo(0));

    [Then("the backend scenario observes its rejected startup's standard error containing {string}")]
    public void ThenTheBackendScenarioObservesItsRejectedStartupsStandardErrorContaining(string text) =>
        Await(myScenario.WaitForStandardErrorContainingAsync(text));

    [Then("the backend scenario observes its rejected startup's standard error does not contain {string}")]
    public void ThenTheBackendScenarioObservesItsRejectedStartupsStandardErrorDoesNotContain(string text) =>
        Assert.That(myScenario.CapturedStandardError(), Does.Not.Contain(text));

    [Then("the backend scenario observes members {string} have distinct sessions")]
    public void ThenTheBackendScenarioObservesMembersHaveDistinctSessions(string commaSeparatedMembers)
    {
        var members = commaSeparatedMembers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sessionIds = members.Select(myScenario.ActiveSessionId).ToArray();
        Assert.That(sessionIds.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(sessionIds.Length));
    }

    [Then("the backend scenario observes members {string} have distinct worktrees")]
    public void ThenTheBackendScenarioObservesMembersHaveDistinctWorktrees(string commaSeparatedMembers)
    {
        var members = commaSeparatedMembers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var worktreePaths = members.Select(myScenario.WorktreePathFor).ToArray();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Assert.That(worktreePaths.Distinct(comparer).Count(), Is.EqualTo(worktreePaths.Length));
    }

    [Then("the backend scenario observes no session was ever started for member {string}")]
    public void ThenTheBackendScenarioObservesNoSessionWasEverStartedForRole(string role) =>
        Assert.That(myScenario.RoleSessionNeverStarted(role), Is.True);

    [Then("the backend scenario observes role {string} received no prompt")]
    public void ThenTheBackendScenarioObservesRoleReceivedNoPrompt(string role) =>
        Assert.That(myScenario.Agent(role).LatestPrompt(), Is.Null);

    [Then("the backend scenario observes role {string} at status {string}")]
    public void ThenTheBackendScenarioObservesRoleAtStatus(string role, string status) =>
        Await(myScenario.WaitForRoleStatusAsync(role, status));

    [Then("the backend scenario observes state.snapshot roles reported in order {string}")]
    public void ThenTheBackendScenarioObservesStateSnapshotRolesReportedInOrder(string commaSeparatedRoles)
    {
        var expected = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var actual = Await(myScenario.LatestSnapshotRoleOrderAsync());
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Then("the backend scenario observes state.snapshot leader as {string}")]
    public void ThenTheBackendScenarioObservesStateSnapshotLeaderAs(string leader) =>
        Assert.That(Await(myScenario.LatestSnapshotLeaderAsync()), Is.EqualTo(leader));

    [Then("the backend scenario observes a session started for member {string} across the control pipe")]
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

    [Then("the {string} agent observes a harness message")]
    public void ThenTheAgentObservesAHarnessMessage(string role) =>
        myObservedHarnessMessage = Await(myScenario.Agent(role).WaitForHarnessMessageAsync());

    [Then("the {string} agent observes a harness message containing {string}")]
    public void ThenTheAgentObservesAHarnessMessageContaining(string role, string expectedSubstring) =>
        myObservedHarnessMessage = Await(myScenario.Agent(role).WaitForHarnessMessageAsync(
            message => message.Contains(expectedSubstring, StringComparison.Ordinal)));

    [Then("the {string} agent observes an abort")]
    public void ThenTheAgentObservesAnAbort(string role) => Await(myScenario.Agent(role).WaitForAbortAsync());

    [Then("the {string} agent has not observed an abort")]
    public void ThenTheAgentHasNotObservedAnAbort(string role) => Assert.That(myScenario.Agent(role).HasObservedAbort(), Is.False);

    [Then("the {string} agent observes {int} aborts")]
    public void ThenTheAgentObservesAborts(string role, int count) => Await(myScenario.Agent(role).WaitForAbortCountAsync(count));

    [Then("the {string} agent has not received the prompt {string} within {int} seconds")]
    public void ThenTheAgentHasNotReceivedThePromptWithinSeconds(string role, string prompt, int seconds) =>
        Assert.CatchAsync<TimeoutException>(
            () => myScenario.Agent(role).WaitForPromptAsync(observed => observed == prompt, TimeSpan.FromSeconds(seconds)));

    [Then("the {string} agent has received no prompt")]
    public void ThenTheAgentHasReceivedNoPrompt(string role) =>
        Assert.That(myScenario.Agent(role).LatestPrompt(), Is.Null);

    [When("the {string} agent holds its next abort pending")]
    public void WhenTheAgentHoldsItsNextAbortPending(string role) =>
        Await(myScenario.Agent(role).ArmPendingAbortAsync());

    [When("the {string} agent releases its pending abort")]
    public void WhenTheAgentReleasesItsPendingAbort(string role) =>
        Await(myScenario.Agent(role).CompletePendingAbortAsync());

    [When("the {string} agent fails its next abort with message {string}")]
    public void WhenTheAgentFailsItsNextAbortWithMessage(string role, string message) =>
        Await(myScenario.Agent(role).FailNextAbortAsync(message));

    [When("the {string} agent holds its next session disposal pending")]
    public void WhenTheAgentHoldsItsNextSessionDisposalPending(string role) =>
        Await(myScenario.Agent(role).ArmPendingDisposalAsync());

    [When("the {string} agent releases its pending session disposal")]
    public void WhenTheAgentReleasesItsPendingSessionDisposal(string role) =>
        Await(myScenario.Agent(role).CompletePendingDisposalAsync());

    [When("the {string} agent holds its next permission response pending")]
    public void WhenTheAgentHoldsItsNextPermissionResponsePending(string role) =>
        Await(myScenario.Agent(role).ArmPendingPermissionResponseAsync());

    [When("the {string} agent fails its pending permission response with message {string}")]
    public void WhenTheAgentFailsItsPendingPermissionResponseWithMessage(string role, string message) =>
        Await(myScenario.Agent(role).FailPendingPermissionResponseAsync(message));

    [Then("the {string} agent's session disposal is held after its admitted send is already canceled")]
    public void ThenTheAgentSSessionDisposalIsHeldAfterItsAdmittedSendIsAlreadyCanceled(string role) =>
        Assert.That(
            Await(myScenario.Agent(role).WaitForDisposalHeldAsync()),
            Is.True,
            $"Role '{role}''s session disposal was held, but its admitted send had not yet reached its own " +
            "canceled outcome by that moment - drain-before-dispose ordering was not observed.");

    // Merely awaiting (without asserting on) the returned flag is deliberate: WaitForDisposalHeldAsync returns
    // whether an admitted send had already reached its own canceled outcome before disposal began - a detail
    // this simpler phrasing does not claim - not whether disposal is held; that itself is proven by this call
    // returning at all rather than timing out.
    [Then("the {string} agent's session disposal is held")]
    public void ThenTheAgentSSessionDisposalIsHeld(string role) =>
        Await(myScenario.Agent(role).WaitForDisposalHeldAsync());

    [Then("role {string} is not ready for a prompt")]
    public void ThenRoleIsNotReadyForAPrompt(string role)
    {
        // A short, independently bounded probe against the same live Headquarters instance proves the role is
        // genuinely not ready yet: it polls Headquarters for its own full timeout before concluding "not ready", so
        // its completion is evidence of a live, contacted Headquarters instance currently reporting this role as
        // not ready - not a guess about how long a fixed sleep should be.
        var probe = myScenario.StartWaitForAgent(role, TimeSpan.FromSeconds(1));
        var probeResult = Await(probe.WaitForCompletionAsync(TimeSpan.FromSeconds(5)));
        Assert.That(probeResult.StdErr, Does.Contain("agent not ready"), () => probeResult.StdErr);
    }

    [Then("the {string} agent observes its pending interactions were cancelled")]
    public void ThenTheAgentObservesItsPendingInteractionsWereCancelled(string role) =>
        Await(myScenario.Agent(role).WaitForPendingInteractionsCancelledAsync());

    [When("the {string} agent requests permission {string} with description {string}")]
    public void WhenTheAgentRequestsPermissionWithDescription(string role, string requestId, string description) =>
        Await(myScenario.Agent(role).RequestPermissionAsync(requestId, description));

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

    [When("the {string} agent requests input {string} with prompt {string} and freeform {string}:")]
    public void WhenTheAgentRequestsInputWithPromptAndFreeform(string role, string requestId, string prompt, string allowFreeform, Table table) =>
        Await(myScenario.Agent(role).RequestInputAsync(requestId, prompt, ChoicesFromRows(table), bool.Parse(allowFreeform)));

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

    [When("the {string} agent requests elicitation {string} with prompt {string}:")]
    public void WhenTheAgentRequestsElicitationWithPromptAndFields(string role, string requestId, string prompt, Table table)
    {
        var row = SingleRow(table, ElicitationRequestColumns, "elicitation request");
        var url = row["url"];
        Await(myScenario.Agent(role).RequestElicitationAsync(requestId, prompt, row["mode"], url.Length == 0 ? null : url));
    }

    [Then("the {string} agent observes an elicitation response for {string} with action {string}:")]
    public void ThenTheAgentObservesAnElicitationResponseForWithActionAndFields(string role, string requestId, string action, Table table)
    {
        var row = SingleRow(table, ElicitationResponseColumns, "elicitation response");
        var formValue = row["form value"];
        var response = Await(myScenario.Agent(role).WaitForElicitationResponseAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.RequestId, Is.EqualTo(requestId));
            Assert.That(response.Action, Is.EqualTo(action));

            if (formValue.Length > 0)
            {
                Assert.That(response.Content?.GetProperty("answer").GetString(), Is.EqualTo(formValue));
            }

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

    // The envelope shape and its exact error text live in the scenario outline's own examples table - this
    // binding only frames a UI-protocol client's wire send, never reinterprets the contract in C#, so a
    // regression in the real UiMessageReader/UiCommandHandler validation pipeline (squad.Ui.Protocol) changes
    // the assertion's outcome rather than silently passing.
    [When("a UI-protocol client sends the invalid {string} envelope:")]
    public void WhenAUiProtocolClientSendsTheInvalidEnvelope(string messageCase, string envelope)
    {
        myLastRejectedCommandEffect = messageCase switch
        {
            "missing request ID" or "invalid boolean payload" => RejectedCommandEffect.PermissionResponse,
            _ => RejectedCommandEffect.Prompt,
        };
        myScenario.SendRawEnvelope(envelope);
    }

    [Then("a UI-protocol client observes the protocol error {string}")]
    public void ThenAUiProtocolClientObservesTheProtocolError(string expectedError)
    {
        var message = Await(myScenario.WaitForProtocolErrorAsync(skip: myProtocolErrorsObserved));
        myProtocolErrorsObserved++;
        Assert.That(message, Is.EqualTo(expectedError));
    }

    // Unlike the structurally invalid envelopes above, this message is well-formed and passes envelope validation;
    // it is rejected only once command dispatch discovers the role has no configured session, so it shares the
    // same exact-error, no-provider-invocation, and still-succeeds assertions as the invalid-envelope matrix
    // without being one of its structural cases.
    [When("a UI-protocol client sends a prompt to the unknown role {string}")]
    public void WhenAUiProtocolClientSendsAPromptToTheUnknownRole(string role)
    {
        myLastRejectedCommandEffect = RejectedCommandEffect.Prompt;
        myScenario.SendPrompt(role, "hello");
    }

    // Every invalid envelope in the matrix is validated (type, role, request id, or payload shape) before command
    // routing ever happens, so the correct proof is that whichever provider-observable effect its command type
    // would otherwise have produced never happened - never a broader "nothing at all happened" sweep the fake
    // session has no API to express.
    [Then("no provider-side command was invoked for the rejected message")]
    public void ThenNoProviderSideCommandWasInvokedForTheRejectedMessage()
    {
        switch (myLastRejectedCommandEffect)
        {
            case RejectedCommandEffect.PermissionResponse:
                Assert.That(myScenario.Agent("coder").HasReceivedPermissionResponse(), Is.False);
                break;
            default:
                Assert.That(myScenario.Agent("coder").LatestPrompt(), Is.Null);
                break;
        }
    }

    [Then("a UI-protocol client's later command still succeeds")]
    public void ThenAUiProtocolClientsLaterCommandStillSucceeds()
    {
        Assert.That(myScenario.IsRunning, Is.True);
        const string prompt = "still usable after the rejected message";
        myScenario.SendPrompt("coder", prompt);
        Assert.That(Await(myScenario.Agent("coder").WaitForPromptAsync(observed => observed == prompt)), Is.EqualTo(prompt));
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

    [When("the {string} agent emits an assistant delta with {int} characters")]
    public void WhenTheAgentEmitsAnAssistantDeltaWithCharacters(string role, int characterCount)
    {
        var state = GetOrCreateTranscriptObservation(role);
        var index = state.AssistantDeltaCount;
        state.AssistantDeltaCount = index + 1;
        Await(myScenario.Agent(role).EmitAssistantAsync(new string('x', characterCount), isDelta: true));

        // Emitting across the control pipe only proves the backend accepted the event, not that the independent
        // background reader has finished appending it onto the archived entry - waiting here for this delta's own
        // transcript update proves it before a later step queries the archived entry's now-stable truncation
        // state. The very first delta for a role creates its entry via an "append" update. Once that entry's
        // in-memory retained buffer has itself been truncated - which a delta this size does immediately -
        // production reports every later continuation as a "replace" (a full retained-entry rebuild), never an
        // "append-content" delta, so later deltas must wait for that operation instead - identified by how many
        // prior "replace" updates this role has already produced, not by content equality, since content this
        // size is expensive to compare and may itself be truncated before it reaches the wire. A larger explicit
        // timeout accounts for the extra time genuinely needed to encode, transmit, and project content this
        // size end to end.
        var timeout = TimeSpan.FromSeconds(60);

        if (index == 0)
        {
            Await(myScenario.WaitForTranscriptUpdateAsync(role, "assistant", content: null, timeout));
        }
        else
        {
            Await(myScenario.WaitForTranscriptUpdateByOperationAsync(role, "replace", content: null, skip: index - 1, timeout: timeout));
        }
    }

    [When("the {string} agent emits a final assistant message {string}")]
    public void WhenTheAgentEmitsAFinalAssistantMessage(string role, string content) =>
        Await(myScenario.Agent(role).EmitAssistantAsync(content, isDelta: false));

    [When("the {string} agent emits a system message {string}")]
    public void WhenTheAgentEmitsASystemMessage(string role, string content) =>
        Await(myScenario.Agent(role).EmitSystemMessageAsync(content));

    [When("the {string} agent emits a system message with {int} characters")]
    public void WhenTheAgentEmitsASystemMessageWithCharacters(string role, int characterCount) =>
        Await(myScenario.Agent(role).EmitSystemMessageAsync(new string('x', characterCount)));

    [When("the {string} agent emits {int} system messages")]
    public void WhenTheAgentEmitsSystemMessages(string role, int count)
    {
        // Sequentially awaiting each emit (rather than firing them concurrently) guarantees "message-{i}" lands at
        // increasing entry indices in publication order - required for the paging assertions that follow to
        // combine synchronization and page entries by their genuine, deterministic content.

        for (var index = 0; index < count; index++)
        {
            Await(myScenario.Agent(role).EmitSystemMessageAsync($"message-{index}"));
        }

        // Emitting across the control pipe only proves the backend accepted the event, not that it has already
        // projected it onto the transcript - the two happen on genuinely independent asynchronous paths. Waiting
        // here for the dashboard protocol to report the very last message of this burst proves every one of them
        // has actually been applied before a later step requests a synchronization, so that request observes this
        // burst's true final boundary rather than an arbitrary, still-catching-up partial state.

        if (count > 0)
        {
            Await(myScenario.WaitForTranscriptUpdateAsync(role, "system", $"message-{count - 1}"));
        }
    }

    [When("the {string} agent emits {int} system messages with {int} characters each")]
    public void WhenTheAgentEmitsSystemMessagesWithCharactersEach(string role, int count, int characterCount)
    {
        var content = new string('x', characterCount);

        for (var index = 0; index < count; index++)
        {
            Await(myScenario.Agent(role).EmitSystemMessageAsync(content));
        }

        // Content this size lands well beyond the production per-entry retained bound, so every one of these
        // updates reports only its live-truncated tail on the wire, never the full raw content - comparing
        // against the exact content sent would therefore never match. Skipping past this burst's own first
        // count-1 "system" appends and matching on operation and role alone, ignoring content, instead proves the
        // very last message of the burst has itself already been applied before a later step requests an archived
        // entry or synchronization, so that request observes this burst's true final boundary rather than an
        // arbitrary, still-catching-up partial state. A generous explicit timeout accounts for the extra time
        // genuinely needed to encode, transmit, and project a burst of entries this size end to end.

        if (count > 0)
        {
            Await(myScenario.WaitForTranscriptUpdateAsync(
                role, "system", content: null, timeout: TimeSpan.FromSeconds(180), skip: count - 1));
        }
    }

    [When("the {string} agent starts subagent {string} displayed as {string} using model {string}")]
    public void WhenTheAgentStartsSubagent(string role, string agentName, string displayName, string model) =>
        Await(myScenario.Agent(role).EmitSubagentStartedAsync(
            NullIfEmpty(agentName), NullIfEmpty(displayName), NullIfEmpty(model)));

    [When("the {string} agent invokes skill {string}")]
    public void WhenTheAgentInvokesSkill(string role, string name) =>
        Await(myScenario.Agent(role).EmitSkillInvokedAsync(name));

    [When("the {string} agent concurrently emits these system messages:")]
    public void WhenTheAgentConcurrentlyEmitsTheseSystemMessages(string role, Table contents) =>
        // Awaiting every row's emit together (rather than sequentially) genuinely races each one against whatever
        // synchronization request a preceding independently-started step already fired, without this step itself
        // waiting on that request's acknowledgement (the protocol has none) or its eventual response.
        Await(Task.WhenAll(contents.Rows.Select(row => myScenario.Agent(role).EmitSystemMessageAsync(row["content"]))));

    private void RecordPagedEntries(string role, IReadOnlyList<TranscriptEntryObservation> entries) =>
        GetOrCreateTranscriptObservation(role).PagedEntries.AddRange(entries);

    private MemberTranscriptObservationState GetOrCreateTranscriptObservation(string role)
    {
        var memberId = new SquadMemberId(role);

        if (!myTranscriptObservations.TryGetValue(memberId, out var state))
        {
            myTranscriptObservations[memberId] = state = new MemberTranscriptObservationState();
        }

        return state;
    }

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
    public void ThenTheTranscriptSynchronizationForRoleIncludesAnEntryWithSourceAndContent(string role, string source, string content)
    {
        var decodedContent = DecodeEscapes(content);
        Await(myScenario.WaitForTranscriptSynchronizationAsync(
            role, entries => entries.Any(entry => entry.Source == source && entry.Content == decodedContent)));
    }

    [Then("the transcript synchronization for role {string} includes exactly these entries:")]
    public void ThenTheTranscriptSynchronizationForRoleIncludesExactlyTheseEntries(string role, Table expected)
    {
        var expectedEntries = expected.Rows.Select(row => (Source: row["source"], Content: DecodeEscapes(row["content"]))).ToList();
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

    [Then("the transcript synchronization for role {string} reports {string} content that is no longer available")]
    public void ThenTheTranscriptSynchronizationForRoleReportsContentThatIsNoLongerAvailable(string role, string source)
    {
        var synchronization = Await(myScenario.WaitForTranscriptSynchronizationAsync(
            role,
            entries => entries.Any(entry =>
                entry.Source == source && entry.Content.Contains("no longer available", StringComparison.Ordinal))));
        Assert.That(
            synchronization.Entries,
            Has.Some.Matches<TranscriptEntryObservation>(entry =>
                entry.Source == source && entry.Content.Contains("no longer available", StringComparison.Ordinal)));
    }

    [Then("the transcript synchronization for role {string} contains exactly {int} entries")]
    public void ThenTheTranscriptSynchronizationForRoleContainsExactlyEntries(string role, int expectedCount)
    {
        var synchronization = Await(myScenario.WaitForTranscriptSynchronizationAsync(role, entries => entries.Count == expectedCount));
        Assert.That(synchronization.Entries, Has.Count.EqualTo(expectedCount));

        // Seeding this role's paging frontier from the live synchronization's oldest entry - never a literal
        // index in the feature file itself - is exactly what keeps the request envelope's "beforeIndex" coordinate
        // private to this step's own bookkeeping, matching how a real reconnecting dashboard would chain a
        // "previous page" request from whatever boundary its own last-known synchronization or page reported.
        var state = GetOrCreateTranscriptObservation(role);
        state.PageFrontier = synchronization.Entries[0].EntryIndex;
        RecordPagedEntries(role, synchronization.Entries);
        state.LatestSynchronizedSequence = synchronization.Sequence;
    }

    [When("the UI-protocol client requests the previous transcript page for role {string}")]
    public void WhenTheUiProtocolClientRequestsThePreviousTranscriptPageForRole(string role)
    {
        var state = GetOrCreateTranscriptObservation(role);

        if (state.PageFrontier is not { } beforeIndex)

        {
            throw new InvalidOperationException(
                $"No transcript synchronization or previous page has been observed yet for role '{role}' to page back from.");
        }

        myScenario.RequestTranscriptPage(role, beforeIndex);
        var skip = state.PagesObserved;
        var page = Await(myScenario.WaitForTranscriptPageAsync(role, skip));
        state.PagesObserved = skip + 1;
        state.LatestPage = page;

        if (page.Entries.Count > 0)
        {
            state.PageFrontier = page.Entries[0].EntryIndex;
        }

        RecordPagedEntries(role, page.Entries);
    }

    [Then("the previous transcript page for role {string} contains exactly {int} entries")]
    public void ThenThePreviousTranscriptPageForRoleContainsExactlyEntries(string role, int expectedCount) =>
        Assert.That(GetOrCreateTranscriptObservation(role).LatestPage!.Entries, Has.Count.EqualTo(expectedCount));

    [Then("the previous transcript page for role {string} reports more history")]
    public void ThenThePreviousTranscriptPageForRoleReportsMoreHistory(string role) =>
        Assert.That(GetOrCreateTranscriptObservation(role).LatestPage!.HasMore, Is.True);

    [Then("the previous transcript page for role {string} reports no more history")]
    public void ThenThePreviousTranscriptPageForRoleReportsNoMoreHistory(string role) =>
        Assert.That(GetOrCreateTranscriptObservation(role).LatestPage!.HasMore, Is.False);

    [When("the UI-protocol client requests the archived transcript entry {int} for role {string}")]
    public void WhenTheUiProtocolClientRequestsTheArchivedTranscriptEntryForRole(int entryIndex, string role)
    {
        myScenario.RequestArchivedEntry(role, entryIndex);
        myLatestArchivedEntry = Await(myScenario.WaitForArchivedEntryAsync(role, entryIndex));
    }

    [Then("the archived transcript entry has content {string}")]
    public void ThenTheArchivedTranscriptEntryHasContent(string content)
    {
        var entry = myLatestArchivedEntry
            ?? throw new InvalidOperationException("No archived transcript entry has been requested yet.");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Content, Is.EqualTo(content));
            Assert.That(entry.ContentTruncated, Is.False);
            Assert.That(entry.TotalContentCharacters, Is.EqualTo(content.Length));
            Assert.That(entry.ArchivedPrefixCharacters, Is.EqualTo(content.Length));
        });
    }

    [Then("the archived transcript entry has {int} characters and is not truncated")]
    public void ThenTheArchivedTranscriptEntryHasCharactersAndIsNotTruncated(int totalCharacters)
    {
        var entry = myLatestArchivedEntry
            ?? throw new InvalidOperationException("No archived transcript entry has been requested yet.");
        Assert.Multiple(() =>
        {
            Assert.That(entry.ContentTruncated, Is.False);
            Assert.That(entry.TotalContentCharacters, Is.EqualTo(totalCharacters));
            Assert.That(entry.ArchivedPrefixCharacters, Is.EqualTo(totalCharacters));
            Assert.That(entry.Content?.Length, Is.EqualTo(totalCharacters));
        });
    }

    [Then("the archived transcript entry is unavailable for role {string}")]
    public void ThenTheArchivedTranscriptEntryIsUnavailableForRole(string role)
    {
        var entry = myLatestArchivedEntry
            ?? throw new InvalidOperationException("No archived transcript entry has been requested yet.");

        if (GetOrCreateTranscriptObservation(role).LatestSynchronizedSequence is not { } expectedSequence)

        {
            throw new InvalidOperationException(
                $"No transcript synchronization has been observed yet for role '{role}' to compare the archived entry's reported sequence against.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(entry.Content, Is.Null);
            Assert.That(entry.ContentTruncated, Is.False);
            Assert.That(entry.TotalContentCharacters, Is.Zero);
            Assert.That(entry.ArchivedPrefixCharacters, Is.Zero);

            // A rotated-out entry still reports the role's true current sequence - proving this reply is a
            // genuine, live protocol answer at the moment of the request, not a stale or fabricated one - it just
            // carries no entry content for this index anymore. Comparing against the sequence a transcript
            // synchronization observed for this role immediately beforehand (with no further activity in
            // between) proves that match precisely, rather than merely that the reported sequence is positive.
            Assert.That(entry.Sequence, Is.EqualTo(expectedSequence));
        });
    }

    [Then("the archived transcript entry is truncated with {int} total characters at the {int} character archive bound")]
    public void ThenTheArchivedTranscriptEntryIsTruncatedWithTotalCharactersAtTheArchiveBound(
        int totalCharacters, int archiveBoundCharacters)
    {
        var entry = myLatestArchivedEntry
            ?? throw new InvalidOperationException("No archived transcript entry has been requested yet.");
        Assert.Multiple(() =>
        {
            Assert.That(entry.ContentTruncated, Is.True);
            Assert.That(entry.TotalContentCharacters, Is.EqualTo(totalCharacters));
            Assert.That(entry.Content, Is.Not.Null);

            // The persisted content's length must land exactly at the production per-entry archive bound -
            // proving the archive stops writing precisely there, not merely somewhere short of the total stream -
            // while the reported prefix (which excludes the appended truncation marker) stays strictly below it.
            Assert.That(entry.Content!.Length, Is.EqualTo(archiveBoundCharacters));
            Assert.That(entry.ArchivedPrefixCharacters, Is.LessThan(archiveBoundCharacters));
        });
    }

    [Then("the transcript update for role {string} reports archived content beyond the {int} character retained bound")]
    public void ThenTheTranscriptUpdateForRoleReportsArchivedContentBeyondTheRetainedBound(string role, int retainedBoundCharacters)
    {
        var update = Await(myScenario.WaitForTranscriptUpdateAsync(role, "system"));
        Assert.Multiple(() =>
        {
            Assert.That(update.HasArchivedContent, Is.True);
            Assert.That(update.ContentStart, Is.GreaterThan(0));

            // The published live content must land exactly at the production per-entry retained bound - proving
            // the update never silently carries more than that bound, rather than merely proving a prefix was
            // skipped and more content exists in the archive.
            Assert.That(update.Content, Is.Not.Null);
            Assert.That(update.Content!.Length, Is.EqualTo(retainedBoundCharacters));
        });
    }

    [Then("the transcript update for role {string} reports a truncated announcement of {int} characters")]
    public void ThenTheTranscriptUpdateForRoleReportsATruncatedAnnouncementOfCharacters(string role, int announcementLength)
    {
        var update = Await(myScenario.WaitForTranscriptUpdateAsync(role, "system"));
        Assert.Multiple(() =>
        {
            Assert.That(update.AnnouncementTruncated, Is.True);
            Assert.That(update.AnnouncementContentLength, Is.EqualTo(announcementLength));
        });
    }

    [Then("the combined transcript history observed for role {string} contains message {int} through message {int} exactly once")]
    public void ThenTheCombinedTranscriptHistoryObservedForRoleContainsMessageThroughMessageExactlyOnce(
        string role, int firstMessage, int lastMessage)
    {
        var expectedContents = Enumerable.Range(firstMessage, lastMessage - firstMessage + 1).Select(index => $"message-{index}").ToList();

        // Comparing as an equivalent multiset (rather than a set) proves every expected message was observed
        // exactly once across every synchronization and page fetched so far for this role - neither missing (an
        // introduced gap) nor repeated (an introduced duplicate) between the live synchronization and however many
        // "previous page" requests it took to page all the way back to the very first entry.
        var observedContents = GetOrCreateTranscriptObservation(role).PagedEntries
            .Select(entry => entry.Content)
            .Where(content => content.StartsWith("message-", StringComparison.Ordinal))
            .ToList();
        Assert.That(observedContents, Is.EquivalentTo(expectedContents));
    }

    [Then("the reconciled transcript for role {string} contains exactly these entries in order:")]
    public void ThenTheReconciledTranscriptForRoleContainsExactlyTheseEntriesInOrder(string role, Table expected)
    {
        var expectedEntries = expected.Rows.Select(row => (Source: row["source"], Content: row["content"])).ToList();
        var expectedSources = expectedEntries.Select(entry => entry.Source).ToHashSet();

        // Reconciling combines the latest transcript synchronization with every update published after its
        // high-water mark, exactly as a reconnecting dashboard client must - proving the synchronization and any
        // publication racing it never lose or duplicate an entry. Filtering to the expected sources (rather than
        // requiring an exact match across the whole transcript) ignores unrelated automatic entries such as the
        // harness "Session started." entry every session publishes.
        var reconciled = Await(myScenario.WaitForReconciledTranscriptAsync(
            role,
            entries => entries.Where(entry => expectedSources.Contains(entry.Source))
                .Select(entry => (entry.Source, entry.Content))
                .SequenceEqual(expectedEntries)));

        var actualEntries = reconciled
            .Where(entry => expectedSources.Contains(entry.Source))
            .Select(entry => (entry.Source, entry.Content))
            .ToList();
        Assert.That(actualEntries, Is.EqualTo(expectedEntries));
    }

    [Then("the reconciled transcript for role {string} contains each of these entries exactly once:")]
    public void ThenTheReconciledTranscriptForRoleContainsEachOfTheseEntriesExactlyOnce(string role, Table expected)
    {
        var expectedEntries = expected.Rows.Select(row => (Source: row["source"], Content: DecodeEscapes(row["content"]))).ToList();
        var expectedSources = expectedEntries.Select(entry => entry.Source).ToHashSet();

        // A concurrent burst of publications may be reconciled in any relative order (genuine concurrency gives no
        // ordering guarantee between the burst's own entries), so this compares as a multiset rather than an
        // ordered sequence - it still proves every expected entry was reconciled exactly once, with none missing
        // or duplicated.
        var reconciled = Await(myScenario.WaitForReconciledTranscriptAsync(
            role,
            entries =>
            {
                var actual = entries.Where(entry => expectedSources.Contains(entry.Source))
                    .Select(entry => (entry.Source, entry.Content))
                    .ToList();
                return actual.Count == expectedEntries.Count
                    && expectedEntries.All(expectedEntry => actual.Count(entry => entry == expectedEntry) == 1);
            }));

        var actualEntries = reconciled
            .Where(entry => expectedSources.Contains(entry.Source))
            .Select(entry => (entry.Source, entry.Content))
            .ToList();
        Assert.That(actualEntries, Is.EquivalentTo(expectedEntries));
        Assert.That(actualEntries, Has.Count.EqualTo(expectedEntries.Count));
    }

    [When("the {string} agent starts tool call {string} named {string}")]
    public void WhenTheAgentStartsToolCallNamed(string role, string toolCallId, string toolName) =>
        Await(myScenario.Agent(role).EmitToolStartedAsync(toolCallId, toolName));

    [When("the {string} agent starts tool call {string} named {string} with arguments:")]
    public void WhenTheAgentStartsToolCallNamedWithArguments(string role, string toolCallId, string toolName, string arguments) =>
        Await(myScenario.Agent(role).EmitToolStartedAsync(toolCallId, toolName, arguments));

    [When("the {string} agent starts tool call {string} named {string} for path {string}")]
    public void WhenTheAgentStartsToolCallNamedForPath(string role, string toolCallId, string toolName, string path) =>
        Await(myScenario.Agent(role).EmitToolStartedAsync(
            toolCallId, toolName, JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = path })));

    [When("the {string} agent starts tool call {string} named {string} for path {string} explicitly kinded as a read")]
    public void WhenTheAgentStartsToolCallNamedForPathExplicitlyKindedAsARead(string role, string toolCallId, string toolName, string path) =>
        Await(myScenario.Agent(role).EmitToolStartedAsync(
            toolCallId, toolName, JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = path }), toolKind: "read"));

    [When("the {string} agent emits tool output {string} for tool call {string}")]
    public void WhenTheAgentEmitsToolOutputForToolCall(string role, string output, string toolCallId) =>
        Await(myScenario.Agent(role).EmitToolOutputChangedAsync(toolCallId, DecodeEscapes(output)));

    [When("the {string} agent completes tool call {string} named {string}")]
    public void WhenTheAgentCompletesToolCallNamed(string role, string toolCallId, string toolName) =>
        Await(myScenario.Agent(role).EmitToolCompletedAsync(toolCallId, toolName, succeeded: true));

    [When("the {string} agent completes tool call {string} named {string} with detailed output {string}")]
    public void WhenTheAgentCompletesToolCallNamedWithDetailedOutput(string role, string toolCallId, string toolName, string detailedOutput) =>
        Await(myScenario.Agent(role).EmitToolCompletedAsync(toolCallId, toolName, succeeded: true, displayOutputFallback: DecodeEscapes(detailedOutput)));

    [When("the {string} agent completes tool call {string} named {string} with display output {string} and content {string}")]
    public void WhenTheAgentCompletesToolCallNamedWithDisplayOutputAndContent(string role, string toolCallId, string toolName, string displayOutput, string content) =>
        Await(myScenario.Agent(role).EmitToolCompletedAsync(
            toolCallId, toolName, succeeded: true,
            displayOutputFallback: DecodeEscapes(displayOutput),
            contentFallback: DecodeEscapes(content)));

    [When("the {string} agent reports progress {string} for tool call {string}")]
    public void WhenTheAgentReportsProgressForToolCall(string role, string progress, string toolCallId) =>
        Await(myScenario.Agent(role).EmitToolProgressAsync(toolCallId, progress));

    [When("the {string} agent emits idle")]
    public void WhenTheAgentEmitsIdle(string role) => Await(myScenario.Agent(role).EmitIdleAsync());

    [When("the {string} agent reports context usage {int} of {int} and AIC usage {decimal}")]
    public void WhenTheAgentReportsContextUsageAndAicUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        Await(myScenario.Agent(role).ReportUsageAsync(contextUsed, contextLimit, aicUsed));

    [When("the {string} agent goes idle with context usage {int} of {int} and AIC usage {decimal}")]
    public void WhenTheAgentGoesIdleWithContextUsageAndAicUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        Await(myScenario.Agent(role).CompleteWithIdleUsageAsync(contextUsed, contextLimit, aicUsed));

    [When("the {string} agent completes its session")]
    public void WhenTheAgentCompletesItsSession(string role) => Await(myScenario.Agent(role).CompleteSessionAsync());

    [When("the {string} agent fails its session with message {string}")]
    public void WhenTheAgentFailsItsSessionWithMessage(string role, string message) =>
        Await(myScenario.Agent(role).FailSessionAsync(message));

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();

    /// <summary>Decodes the literal "\n"/"\r" escape sequences Gherkin step text carries as plain characters into
    /// real newlines/carriage returns, so a scenario can express multi-line tool output on a single step line.</summary>
    private static string DecodeEscapes(string value) =>
        value.Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal);

    /// <summary>Treats an empty step-table cell as an absent (null) value - used for the subagent metadata
    /// columns, where an empty column represents a real production fallback (no agent name, display name, or
    /// model reported), not the literal empty string.</summary>
    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Reads a variable-length "choice" table as a list of individual choice values, or null when the
    /// table has no data rows - representing an input request published or observed without any choices at all,
    /// rather than an empty choices list or a comma-encoded value.</summary>
    private static IReadOnlyList<string>? ChoicesFromRows(Table table)
    {

        if (table.Header.Count != 1 || table.Header.Single() != "choice")
        {
            throw new ArgumentException("choices table must declare exactly one \"choice\" column.");
        }

        return table.RowCount == 0 ? null : table.Rows.Select(row => row["choice"]).ToList();
    }

    private static readonly IReadOnlySet<string> ElicitationRequestColumns = new HashSet<string>(StringComparer.Ordinal) { "mode", "url" };
    private static readonly IReadOnlySet<string> ElicitationResponseColumns = new HashSet<string>(StringComparer.Ordinal) { "form value" };

    /// <summary>Returns the single data row of a record-shaped step table, after validating it declares only its
    /// supported column(s) and exactly one row - one field per column, an empty cell meaning that field is absent,
    /// rather than a comma-encoded value or a second step text variant per combination of present/absent fields.</summary>
    private static DataTableRow SingleRow(Table table, IReadOnlySet<string> supportedColumns, string tableName)
    {
        var unknownColumns = table.Header.Where(column => !supportedColumns.Contains(column)).ToList();

        if (unknownColumns.Count > 0)
        {
            throw new ArgumentException(
                $"{tableName} table declares unsupported column(s): {string.Join(", ", unknownColumns)}. " +
                $"Supported column(s): {string.Join(", ", supportedColumns)}.");
        }

        var missingColumns = supportedColumns.Where(column => !table.Header.Contains(column)).ToList();

        if (missingColumns.Count > 0)
        {
            throw new ArgumentException($"{tableName} table must declare column(s): {string.Join(", ", missingColumns)}.");
        }

        if (table.RowCount != 1)
        {
            throw new ArgumentException($"{tableName} table must declare exactly one row.");
        }

        return table.Rows[0];
    }
}
