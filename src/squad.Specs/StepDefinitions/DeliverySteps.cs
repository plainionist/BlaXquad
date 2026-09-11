using squad.Specs.Support.Scenarios;
using squad.Specs.Support.Mailboxes;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives handoff delivery scenarios exclusively across the real process/protocol boundary: a real "squad handoff"
/// (or, for an otherwise-uncreatable invalid prerequisite, a seeded durable artifact) queued into a role's own
/// worktree, a real, already-launched Headquarters (see <see cref="HeadquartersLifecycleSteps"/>) polling and
/// delivering it through the scenario's own shared <see cref="BackendScenario"/>, and the recipient's own fake
/// session reporting the resulting wake-up harness message across the fake-provider control pipe. Never
/// constructs <c>InProcessHandoffPoller</c>, <c>HandoffDeliveryService</c>, <c>IRoleNotifier</c>, or a role-row
/// product type.
/// </summary>
[Binding]
public sealed class DeliverySteps
{
    /// <summary>The exact wake-up harness message production's <c>SessionRoleNotifier</c> sends a recipient after
    /// a handoff is durably delivered.</summary>
    private const string WakeUpMessage = "You have new handoff mail. If idle, run squad ready-for-next.";
    private const string SenderRole = "coder";
    private const string CreatedAtKey = "handoffCreatedAt";
    private const string EnqueuedAtKey = "handoffEnqueuedAt";
    private const string DequeuedAtKey = "handoffDequeuedAt";

    private readonly ScenarioWorkspace myWorkspace;
    private readonly BackendScenario myScenario;
    private readonly HandoffMailboxObserver myMailbox;

    public DeliverySteps(ScenarioWorkspace workspace, BackendScenario scenario, HandoffMailboxObserver mailbox)
    {
        myWorkspace = workspace;
        myScenario = scenario;
        myMailbox = mailbox;
    }

    [When("{string} durably queues an invalid note to:")]
    public void WhenRoleDurablyQueuesAnInvalidNoteTo(string role, Table recipients) =>
        myMailbox.SeedInvalidOutboundNote(role, string.Join(",", recipients.Rows.Select(row => row["role"])), "Ready for review.");

    [When("{string} durably queues a handoff with invalid content:")]
    public void WhenRoleDurablyQueuesAHandoffWithInvalidContent(string role, string content) =>
        myMailbox.SeedInvalidOutboundContent(role, content);

    [Given("{string} is busy with a prompt")]
    public async Task GivenRoleIsBusyWithAPrompt(string role)
    {
        myScenario.SendPrompt(role, "busy");
        // Wait for the fake session to report the prompt: proof the per-role prompt lock production shares
        // between manual prompts and harness sends is already held, so a delivery wake-up queued after this
        // point cannot possibly race ahead of it.
        await myScenario.Agent(role).WaitForPromptAsync();
    }

    [When("{string} finishes its prompt")]
    public Task WhenRoleFinishesItsPrompt(string role) => myScenario.Agent(role).ReplyAsync("Done.");

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
            "Headquarters to archive the outbound handoff");
        Assert.That(myMailbox.SentHandoffs(SenderRole), Has.Exactly(1).Items);
        Assert.That(myMailbox.FailedHandoffs(SenderRole), Is.Empty);
    }

    [Then("the sender handoff is archived as failed")]
    public void ThenTheSenderHandoffIsArchivedAsFailed()
    {
        myWorkspace.WaitUntil(
            () => myMailbox.SentHandoffs(SenderRole).Count + myMailbox.FailedHandoffCount(SenderRole) == 1,
            "Headquarters to archive the outbound handoff");
        Assert.That(myMailbox.FailedHandoffCount(SenderRole), Is.EqualTo(1));
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

    [Then("the new handoff for {string} carries createdAt and enqueuedAt timestamps but no dequeuedAt or completedAt timestamp")]
    public void ThenTheNewHandoffCarriesCreatedAndEnqueuedTimestamps(string role)
    {
        var handoff = myMailbox.NewInboxHandoffs(role).Single();
        Assert.That(handoff.CreatedAt, Is.Not.Null.And.Not.Empty);
        Assert.That(handoff.EnqueuedAt, Is.Not.Null.And.Not.Empty);
        Assert.That(handoff.DequeuedAt, Is.Null);
        Assert.That(handoff.CompletedAt, Is.Null);
        myWorkspace.Set(CreatedAtKey, handoff.CreatedAt);
        myWorkspace.Set(EnqueuedAtKey, handoff.EnqueuedAt!);
    }

    [When("the {string} role agent claims the handoff via `squad ready-for-next`")]
    public void WhenTheRoleAgentClaimsTheHandoffViaSquadReadyForNext(string role) =>
        myWorkspace.RunRoleTool(role, "squad", ["ready-for-next"]);

    [Then("the in-process handoff for {string} preserves its createdAt and enqueuedAt timestamps and now also carries a dequeuedAt timestamp")]
    public void ThenTheInProcessHandoffPreservesTimestampsAndCarriesDequeuedAt(string role)
    {
        var handoff = myMailbox.InProcessInboxHandoffs(role).Single();
        Assert.That(handoff.CreatedAt, Is.EqualTo(myWorkspace.Get<string>(CreatedAtKey)));
        Assert.That(handoff.EnqueuedAt, Is.EqualTo(myWorkspace.Get<string>(EnqueuedAtKey)));
        Assert.That(handoff.DequeuedAt, Is.Not.Null.And.Not.Empty);
        Assert.That(handoff.CompletedAt, Is.Null);
        myWorkspace.Set(DequeuedAtKey, handoff.DequeuedAt!);
    }

    [When("the {string} role agent completes the handoff via `squad done-with-current`")]
    public void WhenTheRoleAgentCompletesTheHandoffViaSquadDoneWithCurrent(string role) =>
        myWorkspace.RunRoleTool(role, "squad", ["done-with-current"]);

    [Then("the completed handoff for {string} preserves its createdAt, enqueuedAt, and dequeuedAt timestamps and now also carries a completedAt timestamp")]
    public void ThenTheCompletedHandoffPreservesTimestampsAndCarriesCompletedAt(string role)
    {
        var handoff = myMailbox.CompletedInboxHandoffs(role).Single();
        Assert.That(handoff.CreatedAt, Is.EqualTo(myWorkspace.Get<string>(CreatedAtKey)));
        Assert.That(handoff.EnqueuedAt, Is.EqualTo(myWorkspace.Get<string>(EnqueuedAtKey)));
        Assert.That(handoff.DequeuedAt, Is.EqualTo(myWorkspace.Get<string>(DequeuedAtKey)));
        Assert.That(handoff.CompletedAt, Is.Not.Null.And.Not.Empty);
    }

    [Then("the {string} agent observes the handoff wake-up message")]
    public async Task ThenTheAgentObservesTheHandoffWakeUpMessage(string role) =>
        await myScenario.Agent(role).WaitForHarnessMessageAsync(content => content == WakeUpMessage);

    [Then("the {string} agent has not observed the handoff wake-up message")]
    public void ThenTheAgentHasNotObservedTheHandoffWakeUpMessage(string role) =>
        Assert.That(myScenario.Agent(role).LatestHarnessMessage(), Is.Not.EqualTo(WakeUpMessage));

    [Then("the {string} agent's rejected harness send proves no wake-up was delivered")]
    public async Task ThenTheAgentsRejectedHarnessSendProvesNoWakeUpWasDelivered(string role)
    {
        // Wait for Headquarters to have observably attempted (and failed) the harness send, proving absence rather
        // than merely snapshotting state before Headquarters got around to attempting the notification.
        await myScenario.Agent(role).WaitForHarnessRejectedAsync();
        Assert.That(myScenario.Agent(role).LatestHarnessMessage(), Is.Not.EqualTo(WakeUpMessage));
    }

    [Then("Headquarters remains available")]
    public void ThenHeadquartersRemainsAvailable() =>
        Assert.That(myScenario.IsRunning, Is.True);
}
