using squad.Specs.Support;
using squad.Specs.Support.Scenarios;
using squad.AgentProvider.Fake;

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
    private readonly Dictionary<string, int> mySynchronizationSkipByRole = new(StringComparer.Ordinal);
    private IssueDescriptorObservation? myReferencedIssue;

    public StdioUiProtocolSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [When("the operator launches Headquarters with the {string} UI transport")]
    public void WhenTheOperatorLaunchesHeadquartersWithTheUiTransport(string transport)
    {
        if (transport != "stdio")
        {
            throw new NotSupportedException($"Only the 'stdio' UI transport is supported here, not '{transport}'.");
        }
        // The fake provider and its control transport are test setup, not specified behavior - kept behind this
        // binding rather than becoming a second Gherkin dialect, matching HeadquartersLifecycleSteps' own launch.
        myScenario.EnableFakeProviderControl();
        myScenario.LaunchWithoutReadyHandshake<FakeAgentProviderFactory>();
    }

    [When("a UI-protocol client sends \"ui.ready\"")]
    public void WhenAUiProtocolClientSendsUiReady()
    {
        Await(myScenario.CompleteReadyHandshakeAsync());
        foreach (var role in myScenario.ConfiguredRoles)
        {
            // Every session in this feature answers its own prompts automatically ("echo: {prompt}") across the
            // shared fake-provider control pipe instead of a second, narrower provider fixture - arming it here,
            // once the role's session has genuinely started, keeps every later scenario step semantic (prompt in,
            // transcript update out) with no per-prompt reply step of its own.
            Await(myScenario.WaitForRoleSessionStartedAsync(role));
            Await(myScenario.Agent(role).EnableAutoEchoAsync());
        }
    }

    [When("a UI-protocol client sends a {string} command for role {string} with prompt {string}")]
    public void WhenAUiProtocolClientSendsACommandForRoleWithPrompt(string type, string role, string prompt)
    {
        if (type != "prompt.send")
        {
            throw new NotSupportedException($"Only the 'prompt.send' command is supported here, not '{type}'.");
        }
        myScenario.SendPrompt(role, prompt);
    }

    [When("a UI-protocol client requests a transcript page for role {string} before index {int}")]
    public void WhenAUiProtocolClientRequestsATranscriptPageForRoleBeforeIndex(string role, int beforeIndex) =>
        myScenario.RequestTranscriptPage(role, beforeIndex);

    [When("a UI-protocol client requests transcript synchronization")]
    public void WhenAUiProtocolClientRequestsTranscriptSynchronization()
    {
        // Snapshotting each configured role's synchronization count before issuing this request - and later
        // waiting for that count-plus-first one - identifies exactly the "recovery" synchronization this request
        // produced, never the initial one the "ui.ready" handshake already published.
        foreach (var role in myScenario.ConfiguredRoles)
        {
            mySynchronizationSkipByRole[role] = myScenario.CountTranscriptSynchronizations(role);
        }
        myScenario.RequestTranscriptSynchronization();
    }

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
        foreach (var role in myScenario.ConfiguredRoles)
        {
            Await(myScenario.WaitForRoleStatusAsync(role, "idle"));
        }
        Assert.That(myScenario.EveryCapturedStandardOutputLineIsAWellFormedEnvelope(), Is.True);
    }

    [Then("standard error contains no protocol envelope")]
    public void ThenStandardErrorContainsNoProtocolEnvelope() =>
        Assert.That(myScenario.StandardErrorContainsNoProtocolEnvelope(), Is.True);

    [Given("the issues directory exists and is empty")]
    public void GivenTheIssuesDirectoryExistsAndIsEmpty() => myScenario.CreateEmptyIssuesDirectory();

    [Given("the issues directory is replaced with a plain file")]
    public void GivenTheIssuesDirectoryIsReplacedWithAPlainFile() => myScenario.ReplaceIssuesDirectoryWithFile();

    [Given("an issue file {string} with this content:")]
    public void GivenAnIssueFileWithThisContent(string fileName, string content) => myScenario.WriteIssueFile(fileName, content);

    [When("the ui requests the issue catalog with request id {string}")]
    public void WhenTheUiRequestsTheIssueCatalogWithRequestId(string requestId) => myScenario.RequestIssues(requestId);

    [Then("the issue catalog response for request id {string} reports no issues")]
    public void ThenTheIssueCatalogResponseForRequestIdReportsNoIssues(string requestId) =>
        Assert.That(Await(myScenario.WaitForIssuesAsync(requestId)), Is.Empty);

    [Then("the issue catalog response for request id {string} reports issues in this order:")]
    public void ThenTheIssueCatalogResponseForRequestIdReportsIssuesInThisOrder(string requestId, DataTable table)
    {
        var issues = Await(myScenario.WaitForIssuesAsync(requestId));
        Assert.That(issues, Has.Count.EqualTo(table.Rows.Count), "Unexpected number of catalog entries.");
        for (var index = 0; index < table.Rows.Count; index++)
        {
            var row = table.Rows[index];
            var issue = issues[index];
            var expectedPriority = string.IsNullOrEmpty(row["priority"]) ? (int?)null : int.Parse(row["priority"]);
            Assert.That(issue.Path, Is.EqualTo(row["path"]), $"Unexpected path at position {index}.");
            Assert.That(issue.Title, Is.EqualTo(row["title"]), $"Unexpected title at position {index}.");
            Assert.That(issue.Priority, Is.EqualTo(expectedPriority), $"Unexpected priority at position {index}.");
        }
    }

    [Then("the issue catalog response for request id {string} includes an issue at path {string} with frontmatter:")]
    public void ThenTheIssueCatalogResponseForRequestIdIncludesAnIssueAtPathWithFrontmatter(
        string requestId, string path, string frontmatter)
    {
        var issues = Await(myScenario.WaitForIssuesAsync(requestId));
        myReferencedIssue = issues.SingleOrDefault(issue => issue.Path == path);
        Assert.That(myReferencedIssue, Is.Not.Null, $"No issue at path '{path}' was reported.");
        // The docstring itself may carry "\r\n" line endings depending on how the feature file was checked out;
        // the real catalog always normalizes them to "\n", so normalize the expectation the same way here.
        var normalizedFrontmatter = frontmatter.Replace("\r\n", "\n").Replace("\r", "\n");
        Assert.That(myReferencedIssue!.Frontmatter, Is.EqualTo(normalizedFrontmatter));
    }

    [Then("the issue catalog response for request id {string} includes an issue at path {string} with empty frontmatter")]
    public void ThenTheIssueCatalogResponseForRequestIdIncludesAnIssueAtPathWithEmptyFrontmatter(string requestId, string path)
    {
        var issues = Await(myScenario.WaitForIssuesAsync(requestId));
        myReferencedIssue = issues.SingleOrDefault(issue => issue.Path == path);
        Assert.That(myReferencedIssue, Is.Not.Null, $"No issue at path '{path}' was reported.");
        Assert.That(myReferencedIssue!.Frontmatter, Is.Empty);
    }

    [Then("that issue reports these preview lines:")]
    public void ThenThatIssueReportsThesePreviewLines(DataTable table)
    {
        Assert.That(myReferencedIssue, Is.Not.Null, "No issue has been referenced yet.");
        var expectedLines = table.Rows.Select(row => row["line"]).ToArray();
        Assert.That(myReferencedIssue!.PreviewLines, Is.EqualTo(expectedLines));
    }

    [Then("a correlated protocol error for request id {string} is reported")]
    public void ThenACorrelatedProtocolErrorForRequestIdIsReported(string requestId) =>
        Assert.That(Await(myScenario.WaitForCorrelatedProtocolErrorAsync(requestId)), Is.Not.Empty);

    private static void Await(Task task) => task.GetAwaiter().GetResult();

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
