using squad.Specs.Support;

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

    [Then("the dashboard shows role {string}'s latest status as {string}")]
    public async Task ThenTheDashboardShowsRoleSLatestStatusAs(string role, string status) =>
        await myScenario.WaitForLatestRoleStatusAsync(role, status);

    [Then("the dashboard shows a pending permission {string} for role {string} with description {string}")]
    public async Task ThenTheDashboardShowsAPendingPermissionForRoleWithDescription(string requestId, string role, string description) =>
        await myScenario.WaitForPendingPermissionAsync(role, requestId, description);

    [Then("the dashboard shows no pending permission {string} for role {string}")]
    public async Task ThenTheDashboardShowsNoPendingPermissionForRole(string requestId, string role) =>
        await myScenario.WaitForNoPendingPermissionAsync(role, requestId);

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

    [Then("the freshly synchronized transcript for role {string} does not contain {string}")]
    public void ThenTheFreshlySynchronizedTranscriptForRoleDoesNotContain(string role, string content)
    {
        Assert.That(myAwaitedTranscriptSynchronization, Is.Not.Null);
        Assert.That(myAwaitedTranscriptSynchronization!.Role, Is.EqualTo(role));
        Assert.That(
            myAwaitedTranscriptSynchronization.Entries.Any(entry => entry.Content.Contains(content, StringComparison.Ordinal)),
            Is.False);
    }
}
