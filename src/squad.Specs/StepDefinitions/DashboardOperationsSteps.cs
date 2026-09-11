using squad.Specs.Support;
using squad.Specs.Support.Ui;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Dashboard-user language for sending a role-directed prompt, aborting a role's work, and observing the resulting
/// role status, pending permissions, transcript content, or protocol error - the "Dashboard operations" canonical
/// vocabulary module: a user sends prompts, aborts work, and the dashboard shows role state, interactions, and
/// transcript content. Requests the scenario's single <see cref="BackendScenario"/> instance rather than
/// constructing its own, so it observes the same running process other language modules' steps drive. Drives the
/// real production UI protocol and awaits the real production transcript projection through that shared facade -
/// never a test-owned actor name or the fake-agent control pipe - so this vocabulary is safe to reuse from any
/// future migrated feature needing the same dashboard behavior.
/// </summary>
[Binding]
public sealed class DashboardOperationsSteps
{
    private readonly BackendScenario myScenario;
    private int myProtocolErrorsObserved;
    private TranscriptSynchronizationObservation? myAwaitedTranscriptSynchronization;
    private readonly List<TranscriptUpdateObservation> myReceivedTranscriptUpdates = [];

    public DashboardOperationsSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [When("the user sends {string} to role {string}")]
    public void WhenTheUserSendsToRole(string prompt, string role) => myScenario.SendPrompt(role, prompt);

    [When("the user aborts role {string}")]
    public void WhenTheUserAbortsRole(string role) => myScenario.RequestAbort(role);

    [Then("the transcript for role {string} contains {string}")]
    public async Task ThenTheTranscriptForRoleContains(string role, string content) =>
        await myScenario.WaitForTranscriptAsync(role, content);

    [Then("the transcript for role {string} does not contain {string} within {int} seconds")]
    public void ThenTheTranscriptForRoleDoesNotContainWithinSeconds(string role, string content, int seconds) =>
        Assert.CatchAsync<TimeoutException>(
            () => myScenario.WaitForTranscriptAsync(role, content, TimeSpan.FromSeconds(seconds)));

    [Then("the dashboard receives a transcript update for role {string} with source {string}")]
    public async Task ThenTheDashboardReceivesATranscriptUpdateForRoleWithSource(string role, string source) =>
        myReceivedTranscriptUpdates.Add(await myScenario.WaitForTranscriptUpdateAsync(role, source));

    [Then("the dashboard receives a transcript update for role {string} with source {string} and content {string}")]
    public async Task ThenTheDashboardReceivesATranscriptUpdateForRoleWithSourceAndContent(string role, string source, string content) =>
        myReceivedTranscriptUpdates.Add(await myScenario.WaitForTranscriptUpdateAsync(role, source, DecodeEscapes(content)));

    [Then("the dashboard receives a transcript update for role {string} with operation {string} and content {string}")]
    public async Task ThenTheDashboardReceivesATranscriptUpdateForRoleWithOperationAndContent(string role, string operation, string content) =>
        myReceivedTranscriptUpdates.Add(await myScenario.WaitForTranscriptUpdateByOperationAsync(role, operation, DecodeEscapes(content)));

    [Then("the most recently received transcript updates for role {string} report the same entry index")]
    public void ThenTheMostRecentlyReceivedTranscriptUpdatesForRoleReportTheSameEntryIndex(string role)
    {
        var (previous, current) = TwoMostRecentlyReceivedTranscriptUpdates(role);
        Assert.That(current.EntryIndex, Is.EqualTo(previous.EntryIndex));
    }

    [Then("the most recently received transcript updates for role {string} report different entry indices")]
    public void ThenTheMostRecentlyReceivedTranscriptUpdatesForRoleReportDifferentEntryIndices(string role)
    {
        var (previous, current) = TwoMostRecentlyReceivedTranscriptUpdates(role);
        Assert.That(current.EntryIndex, Is.Not.EqualTo(previous.EntryIndex));
    }

    [Then("every transcript update received for role {string} reports a strictly increasing sequence and entry index")]
    public void ThenEveryTranscriptUpdateReceivedForRoleReportsAStrictlyIncreasingSequenceAndEntryIndex(string role)
    {
        var updatesForRole = myReceivedTranscriptUpdates.Where(update => update.Role == role).ToList();
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

    private (TranscriptUpdateObservation Previous, TranscriptUpdateObservation Current) TwoMostRecentlyReceivedTranscriptUpdates(string role)
    {
        var updatesForRole = myReceivedTranscriptUpdates.Where(update => update.Role == role).ToList();
        Assert.That(updatesForRole, Has.Count.GreaterThanOrEqualTo(2));
        return (updatesForRole[^2], updatesForRole[^1]);
    }

    [Then("the user observes a protocol error mentioning {string}")]
    public async Task ThenTheUserObservesAProtocolErrorMentioning(string text)
    {
        var message = await myScenario.WaitForProtocolErrorAsync(skip: myProtocolErrorsObserved);
        myProtocolErrorsObserved++;
        Assert.That(message, Does.Contain(text));
    }

    [Then("the dashboard shows role {string} at status {string}")]
    public async Task ThenTheDashboardShowsRoleAtStatus(string role, string status) =>
        await myScenario.WaitForRoleStatusAsync(role, status);

    [Then("the dashboard shows role {string} as working with context usage {int} of {int} and AIC usage {decimal}")]
    public async Task ThenTheDashboardShowsRoleAsWorkingWithUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        await myScenario.WaitForRoleUsageSnapshotAsync(role, isWorking: true, contextUsed, contextLimit, aicUsed);

    [Then("the dashboard shows role {string} as idle with context usage {int} of {int} and AIC usage {decimal}")]
    public async Task ThenTheDashboardShowsRoleAsIdleWithUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        await myScenario.WaitForRoleUsageSnapshotAsync(role, isWorking: false, contextUsed, contextLimit, aicUsed);

    [Then("the dashboard does not show role {string} at AIC usage {decimal} within {int} seconds")]
    public void ThenTheDashboardDoesNotShowRoleAtAicUsageWithinSeconds(string role, decimal aicUsed, int seconds) =>
        Assert.CatchAsync<TimeoutException>(() => myScenario.WaitForRoleUsageAsync(role, aicUsed, TimeSpan.FromSeconds(seconds)));

    [Then("the dashboard shows role {string}'s latest status as {string}")]
    public async Task ThenTheDashboardShowsRoleSLatestStatusAs(string role, string status) =>
        await myScenario.WaitForLatestRoleStatusAsync(role, status);

    [Then("the dashboard shows role {string} with active tool {string}")]
    public async Task ThenTheDashboardShowsRoleWithActiveTool(string role, string tool) =>
        await myScenario.WaitForRoleActiveToolAsync(role, tool);

    [Then("the dashboard shows role {string} with no active tool")]
    public async Task ThenTheDashboardShowsRoleWithNoActiveTool(string role) =>
        await myScenario.WaitForNoActiveToolAsync(role);

    [Then("the dashboard shows a pending permission {string} for role {string} with description {string}")]
    public async Task ThenTheDashboardShowsAPendingPermissionForRoleWithDescription(string requestId, string role, string description) =>
        await myScenario.WaitForPendingPermissionAsync(role, requestId, description);

    [Then("the dashboard shows no pending permission {string} for role {string}")]
    public async Task ThenTheDashboardShowsNoPendingPermissionForRole(string requestId, string role) =>
        await myScenario.WaitForNoPendingPermissionAsync(role, requestId);

    [Then("the dashboard shows a pending input {string} for role {string} with prompt {string} and freeform {string}:")]
    public async Task ThenTheDashboardShowsAPendingInputForRoleWithPromptAndFreeform(
        string requestId, string role, string prompt, string allowFreeform, Table table) =>
        await myScenario.WaitForPendingInputAsync(role, requestId, prompt, ChoicesFromRows(table), bool.Parse(allowFreeform));

    [Then("the dashboard shows a pending elicitation {string} for role {string} with prompt {string}:")]
    public async Task ThenTheDashboardShowsAPendingElicitationForRoleWithPrompt(string requestId, string role, string prompt, Table table)
    {
        var row = SingleRow(table, ElicitationRequestColumns, "pending elicitation");
        var url = row["url"];
        await myScenario.WaitForPendingElicitationAsync(role, requestId, prompt, row["mode"], url.Length == 0 ? null : url);
    }

    [When("the user responds to permission {string} for role {string} with approved {string}")]
    public void WhenTheUserRespondsToPermissionForRoleWithApproved(string requestId, string role, string approved) =>
        myScenario.RespondToPermission(role, requestId, bool.Parse(approved));

    [When("the user responds to input {string} for role {string} with answer {string}")]
    public void WhenTheUserRespondsToInputForRoleWithAnswer(string requestId, string role, string answer) =>
        myScenario.RespondToInput(role, requestId, answer);

    [When("the user responds to elicitation {string} for role {string} with action {string}:")]
    public void WhenTheUserRespondsToElicitationForRoleWithAction(string requestId, string role, string action, Table table)
    {
        var row = SingleRow(table, ElicitationResponseColumns, "elicitation response");
        var formValue = row["form value"];
        myScenario.RespondToElicitation(role, requestId, action, formValue.Length == 0 ? null : new { answer = formValue });
    }

    [When("the user requests a fresh transcript synchronization for role {string}")]
    public async Task WhenTheUserRequestsAFreshTranscriptSynchronizationForRole(string role)
    {
        // Snapshotting the count of synchronizations already captured for this role - before issuing the request -
        // and then waiting for that count-plus-first one to appear identifies exactly the response this specific
        // request produced (never an earlier one, such as the initial "ui.ready" handshake, that happened to
        // already satisfy some later content assertion).
        var skip = myScenario.CountTranscriptSynchronizations(role);
        myScenario.RequestTranscriptSynchronization();
        myAwaitedTranscriptSynchronization = await myScenario.WaitForNextTranscriptSynchronizationAsync(role, skip);
    }

    [When("the user begins a fresh transcript synchronization")]
    public void WhenTheUserBeginsAFreshTranscriptSynchronization() =>
        // Fires the request without awaiting any acknowledgement (the protocol has none) or a later synchronization
        // response - the independently-started half of a reusable race operation, composed with a following ordered
        // event-emission step so a scenario can race this request against concurrent publication.
        myScenario.RequestTranscriptSynchronization();

    [Then("the freshly synchronized transcript for role {string} does not contain {string}")]
    public void ThenTheFreshlySynchronizedTranscriptForRoleDoesNotContain(string role, string content)
    {
        Assert.That(myAwaitedTranscriptSynchronization, Is.Not.Null);
        Assert.That(myAwaitedTranscriptSynchronization!.Role, Is.EqualTo(role));
        Assert.That(
            myAwaitedTranscriptSynchronization.Entries.Any(entry => entry.Content.Contains(content, StringComparison.Ordinal)),
            Is.False);
    }

    [Then("the freshly synchronized transcript for role {string} contains an entry with source {string} and content {string}")]
    public void ThenTheFreshlySynchronizedTranscriptForRoleContainsAnEntryWithSourceAndContent(string role, string source, string content)
    {
        Assert.That(myAwaitedTranscriptSynchronization, Is.Not.Null);
        Assert.That(myAwaitedTranscriptSynchronization!.Role, Is.EqualTo(role));
        Assert.That(
            myAwaitedTranscriptSynchronization.Entries.Any(entry => entry.Source == source && entry.Content == content),
            Is.True);
    }

    private static readonly IReadOnlySet<string> ElicitationRequestColumns = new HashSet<string>(StringComparer.Ordinal) { "mode", "url" };
    private static readonly IReadOnlySet<string> ElicitationResponseColumns = new HashSet<string>(StringComparer.Ordinal) { "form value" };

    /// <summary>Decodes the literal "\n"/"\r" escape sequences a step's string argument may contain - Reqnroll
    /// passes step text through verbatim, so a Gherkin step written with an escape sequence would otherwise never
    /// match the real newline character production actually reports.</summary>
    private static string DecodeEscapes(string value) =>
        value.Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal);

    /// <summary>Reads a variable-length "choice" table as a list of individual choice values, or null when the
    /// table has no data rows - representing a pending input shown without any choices at all, rather than an
    /// empty choices list or a comma-encoded value.</summary>
    private static IReadOnlyList<string>? ChoicesFromRows(Table table)
    {
        if (table.Header.Count != 1 || table.Header.Single() != "choice")
        {
            throw new ArgumentException("choices table must declare exactly one \"choice\" column.");
        }

        return table.RowCount == 0 ? null : table.Rows.Select(row => row["choice"]).ToList();
    }

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
