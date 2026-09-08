using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives handoff delivery scenarios exclusively across the real process/protocol boundary: a real "squad handoff"
/// (or, for an otherwise-uncreatable invalid prerequisite, a seeded durable artifact) queued into a role's own
/// worktree, a real squad-hq host polling and delivering it, and the recipient's own fake session reporting the
/// resulting wake-up harness message across the fake-provider control pipe. Never constructs
/// <c>InProcessHandoffPoller</c>, <c>HandoffDeliveryService</c>, <c>IRoleNotifier</c>, or a role-row product type.
/// </summary>
[Binding]
public sealed class DeliverySteps
{
    /// <summary>The exact wake-up harness message production's <c>SessionRoleNotifier</c> sends a recipient after
    /// a handoff is durably delivered.</summary>
    private const string WakeUpMessage = "You have new handoff mail. If idle, run squad ready-for-next.";
    private const string SenderRole = "coder";

    private readonly ScenarioWorkspace myWorkspace;
    private readonly BackendScenario myScenario;
    private readonly HandoffMailboxObserver myMailbox;

    public DeliverySteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
        myScenario = new BackendScenario(workspace);
        myMailbox = new HandoffMailboxObserver(workspace);
    }

    [AfterScenario]
    public void CleanUp() => myScenario.Dispose();

    [Given("a running squad host for roles {string}")]
    public async Task GivenARunningSquadHostForRoles(string commaSeparatedRoles)
    {
        var roles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myScenario.ConfigureRoles(roles);
        myScenario.EnableFakeProviderControl();
        await myScenario.StartAsync<FakeAgentProviderFactory>();
        foreach (var role in roles)
        {
            await myScenario.WaitForRoleSessionStartedAsync(role);
        }
    }

    [When("{string} durably queues an invalid note to {string}")]
    public void WhenRoleDurablyQueuesAnInvalidNoteTo(string role, string recipients) =>
        myMailbox.SeedInvalidOutboundNote(role, recipients, "Ready for review.");

    [Given("the {string} agent will reject its next harness send")]
    public async Task GivenTheAgentWillRejectItsNextHarnessSend(string role)
    {
        // Wait for the session-start harness instruction first, so the armed rejection targets the next harness
        // send after it (the delivery wake-up) rather than racing that very first instruction.
        await myScenario.Agent(role).WaitForHarnessMessageAsync();
        await myScenario.Agent(role).RejectNextHarnessAsync();
    }

    [Then("the sender handoff is archived as sent")]
    public void ThenTheSenderHandoffIsArchivedAsSent()
    {
        myWorkspace.WaitUntil(
            () => myMailbox.SentHandoffs(SenderRole).Count + myMailbox.FailedHandoffs(SenderRole).Count == 1,
            "the host to archive the outbound handoff");
        Assert.That(myMailbox.SentHandoffs(SenderRole), Has.Exactly(1).Items);
        Assert.That(myMailbox.FailedHandoffs(SenderRole), Is.Empty);
    }

    [Then("the sender handoff is archived as failed")]
    public void ThenTheSenderHandoffIsArchivedAsFailed()
    {
        myWorkspace.WaitUntil(
            () => myMailbox.SentHandoffs(SenderRole).Count + myMailbox.FailedHandoffs(SenderRole).Count == 1,
            "the host to archive the outbound handoff");
        Assert.That(myMailbox.FailedHandoffs(SenderRole), Has.Exactly(1).Items);
    }

    [Then("{string} has one new handoff")]
    public void ThenRoleHasOneNewHandoff(string role) =>
        Assert.That(myMailbox.NewInboxHandoffs(role), Has.Exactly(1).Items);

    [Then("{string} has no new handoff")]
    public void ThenRoleHasNoNewHandoff(string role) =>
        Assert.That(myMailbox.NewInboxHandoffs(role), Is.Empty);

    [Then("the new handoff for {string} has recipient header {string}")]
    public void ThenTheNewHandoffHasRecipientHeader(string role, string recipient) =>
        Assert.That(myMailbox.NewInboxHandoffs(role).Single().Recipient, Is.EqualTo(recipient));

    [Then("the {string} agent observes the handoff wake-up message")]
    public async Task ThenTheAgentObservesTheHandoffWakeUpMessage(string role) =>
        await myScenario.Agent(role).WaitForHarnessMessageAsync(content => content == WakeUpMessage);

    [Then("the {string} agent has not observed the handoff wake-up message")]
    public void ThenTheAgentHasNotObservedTheHandoffWakeUpMessage(string role) =>
        Assert.That(myScenario.Agent(role).LatestHarnessMessage(), Is.Not.EqualTo(WakeUpMessage));

    [Then("the {string} agent's rejected harness send proves no wake-up was delivered")]
    public async Task ThenTheAgentsRejectedHarnessSendProvesNoWakeUpWasDelivered(string role)
    {
        // Wait for the host to have observably attempted (and failed) the harness send, proving absence rather
        // than merely snapshotting state before the host got around to attempting the notification.
        await myScenario.Agent(role).WaitForHarnessRejectedAsync();
        Assert.That(myScenario.Agent(role).LatestHarnessMessage(), Is.Not.EqualTo(WakeUpMessage));
    }

    [Then("the squad host remains available")]
    public void ThenTheSquadHostRemainsAvailable() =>
        Assert.That(myScenario.IsRunning, Is.True);
}
