using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the real, separately launched "squad-hq --ui stdio" process through the shared <see cref="BackendScenario"/>
/// process driver and the shared <see cref="FakeAgentProviderFactory"/> fixture - proving the real UI protocol's raw
/// wire framing (the ready-handshake gate, transcript synchronization and paging, host-controlled shutdown, and
/// stdout/stderr separation) that no semantic wait already covers. No step here parses protocol envelopes, touches
/// raw JSON, reads a stream, or otherwise manages the child process directly; every role's automatic "echo: {prompt}"
/// reply crosses the shared fake-provider control pipe instead of a second, narrower provider fixture.
/// </summary>
[Binding]
public sealed class StdioUiProtocolSteps
{
    private static readonly TimeSpan PreReadyGraceWindow = TimeSpan.FromSeconds(2);

    private readonly BackendScenario myScenario;
    private readonly List<string> myRoles = [];
    private readonly Dictionary<string, int> mySynchronizationSkipByRole = new(StringComparer.Ordinal);
    private int myExitCode;

    public StdioUiProtocolSteps(ScenarioWorkspace workspace)
    {
        myScenario = new BackendScenario(workspace);
    }

    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

    [Given("a git project prepared with a {string} role using the fake provider fixture")]
    public void GivenAGitProjectPreparedWithARoleUsingTheFakeProviderFixture(string role)
    {
        myRoles.Add(role);
        myScenario.ConfigureRole(role);
        myScenario.EnableFakeProviderControl();
    }

    [Given("a git project prepared with {string} and {string} roles using the fake provider fixture")]
    public void GivenAGitProjectPreparedWithRolesUsingTheFakeProviderFixture(string firstRole, string secondRole)
    {
        myRoles.Add(firstRole);
        myRoles.Add(secondRole);
        myScenario.ConfigureRoles(firstRole, secondRole);
        myScenario.EnableFakeProviderControl();
    }

    [When("squad-hq is launched with \"--ui stdio\"")]
    public void WhenSquadHqIsLaunchedWithUiStdio() => myScenario.LaunchWithoutReadyHandshake<FakeAgentProviderFactory>();

    [When("the ui sends \"ui.ready\"")]
    public void WhenTheUiSendsUiReady()
    {
        Await(myScenario.CompleteReadyHandshakeAsync());
        foreach (var role in myRoles)
        {
            // Every session in this feature answers its own prompts automatically ("echo: {prompt}") across the
            // shared fake-provider control pipe instead of a second, narrower provider fixture - arming it here,
            // once the role's session has genuinely started, keeps every later scenario step semantic (prompt in,
            // transcript update out) with no per-prompt reply step of its own.
            Await(myScenario.WaitForRoleSessionStartedAsync(role));
            Await(myScenario.Agent(role).EnableAutoEchoAsync());
        }
    }

    [When("the ui sends a {string} command for role {string} with prompt {string}")]
    public void WhenTheUiSendsACommandForRoleWithPrompt(string type, string role, string prompt)
    {
        if (type != "prompt.send")
        {
            throw new NotSupportedException($"Only the 'prompt.send' command is supported here, not '{type}'.");
        }
        myScenario.SendPrompt(role, prompt);
    }

    [When("the ui requests a transcript page for role {string} before index {int}")]
    public void WhenTheUiRequestsATranscriptPageForRoleBeforeIndex(string role, int beforeIndex) =>
        myScenario.RequestTranscriptPage(role, beforeIndex);

    [When("the ui requests transcript synchronization")]
    public void WhenTheUiRequestsTranscriptSynchronization()
    {
        // Snapshotting each configured role's synchronization count before issuing this request - and later
        // waiting for that count-plus-first one - identifies exactly the "recovery" synchronization this request
        // produced, never the initial one the "ui.ready" handshake already published.
        foreach (var role in myRoles)
        {
            mySynchronizationSkipByRole[role] = myScenario.CountTranscriptSynchronizations(role);
        }
        myScenario.RequestTranscriptSynchronization();
    }

    [When("squad-hq requests shutdown for the workspace")]
    public void WhenSquadHqRequestsShutdownForTheWorkspace() => myExitCode = Await(myScenario.ShutdownAsync());

    [Then("the shutdown request succeeds")]
    public void ThenTheShutdownRequestSucceeds() => Assert.That(myExitCode, Is.Zero);

    [Then("the squad-hq process exits with code {string}")]
    public void ThenTheSquadHqProcessExitsWithCode(string expectedExitCode) =>
        Assert.That(myExitCode, Is.EqualTo(int.Parse(expectedExitCode)));

    [Then("no protocol message is written to stdout yet")]
    public void ThenNoProtocolMessageIsWrittenToStdoutYet()
    {
        // A single fixed-delay check can pass trivially if launch preparation (workspace/provider/sleep-inhibitor
        // setup) is still running when it fires, proving nothing about the ui.ready gate. Poll continuously across
        // a bounded window generous enough to span that preparation instead, and fail the instant any line appears.
        var deadline = DateTime.UtcNow + PreReadyGraceWindow;
        while (DateTime.UtcNow < deadline)
        {
            Assert.That(myScenario.CapturedStandardOutput(), Is.Empty, "Protocol output appeared before \"ui.ready\" was sent.");
            Thread.Sleep(25);
        }
    }

    [Then("an initial \"transcript.synchronize\" message for role {string} is written to stdout")]
    public void ThenAnInitialTranscriptSynchronizeMessageForRoleIsWrittenToStdout(string role) =>
        Await(myScenario.WaitForNextTranscriptSynchronizationAsync(role, skip: 0));

    [Then("a recovery \"transcript.synchronize\" message for role {string} is written to stdout")]
    public void ThenARecoveryTranscriptSynchronizeMessageForRoleIsWrittenToStdout(string role) =>
        Await(myScenario.WaitForNextTranscriptSynchronizationAsync(role, mySynchronizationSkipByRole[role]));

    [Then("a \"state.snapshot\" message is written to stdout")]
    public void ThenAStateSnapshotMessageIsWrittenToStdout() =>
        Assert.That(myScenario.IsReady, Is.True, "The ready handshake must have already observed a state.snapshot message.");

    [Then("a \"transcript.update\" message for role {string} with content {string} is written to stdout")]
    public void ThenATranscriptUpdateMessageForRoleWithContentIsWrittenToStdout(string role, string content) =>
        Await(myScenario.WaitForTranscriptAsync(role, content));

    [Then("a \"transcript.page\" message for role {string} is written to stdout")]
    public void ThenATranscriptPageMessageForRoleIsWrittenToStdout(string role) =>
        Await(myScenario.WaitForTranscriptPageAsync(role));

    [Then("every stdout line is a well-formed protocol envelope")]
    public void ThenEveryStdoutLineIsAWellFormedProtocolEnvelope()
    {
        // Guards against a race where the echoed transcript update has not yet reached stdout: wait for every
        // configured role to settle back to idle before checking every captured line's shape, rather than
        // asserting well-formedness against a possibly still-partial buffer.
        foreach (var role in myRoles)
        {
            Await(myScenario.WaitForRoleStatusAsync(role, "idle"));
        }
        Assert.That(myScenario.EveryCapturedStandardOutputLineIsAWellFormedEnvelope(), Is.True);
    }

    [Then("standard error contains no protocol envelope")]
    public void ThenStandardErrorContainsNoProtocolEnvelope() =>
        Assert.That(myScenario.StandardErrorContainsNoProtocolEnvelope(), Is.True);

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
