using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives durable-delivery recovery scenarios exclusively across the real process/protocol boundary: a real
/// "squad handoff" queued into a role's own worktree, durable inbox fixtures seeded directly on disk for
/// otherwise-uncreatable "already delivered" or "pre-existing" prerequisites, a real squad-hq host (and, for
/// restart scenarios, a second independently launched replacement host against the same workspace), and the
/// recipient's own fake session reporting wake-up harness messages across the fake-provider control pipe. Never
/// constructs <c>InProcessHandoffPoller</c>, <c>HandoffDeliveryService</c>, <c>IRoleNotifier</c>, or a role-row
/// product type.
/// </summary>
[Binding]
public sealed class RecoverySteps
{
    /// <summary>The exact wake-up harness message production's <c>SessionRoleNotifier</c> sends a recipient after
    /// a handoff is durably delivered or recovered at startup.</summary>
    private const string WakeUpMessage = "You have new handoff mail. If idle, run squad ready-for-next.";
    /// <summary>A validly-formatted, but distinctly-payloaded, recipient artifact: parseable like a real delivery
    /// (so <see cref="HandoffMailboxObserver.NewInboxHandoffs"/> can still enumerate it), yet never producible by
    /// a fresh delivery render, which always carries the original message body and its own "enqueued_at" header.
    /// Proves an "already persisted" recipient copy survives a retried delivery completely untouched.</summary>
    private const string RecipientCopyMarker =
        "id: already-delivered-marker\nfrom: coder\nto: reviewer\nrecipient: reviewer\npriority: 50\ntype: note\nmessage: Already delivered marker\n\nAlready delivered marker\n";
    private const string SnapshotKey = "recoveryInboxSnapshot";

    private readonly ScenarioWorkspace myWorkspace;
    private readonly HandoffMailboxObserver myMailbox;
    private readonly HandoffDraftWriter myDrafts;
    private readonly TaskMailboxFixture myTaskMailbox;
    private BackendScenario? myScenario;
    private BackendScenario? myReplacementScenario;
    private BackendScenario? myCurrentScenario;
    private string[] myRoles = [];

    public RecoverySteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
        myMailbox = new HandoffMailboxObserver(workspace);
        myDrafts = new HandoffDraftWriter(workspace);
        myTaskMailbox = new TaskMailboxFixture(workspace);
    }

    [AfterScenario]
    public void CleanUp()
    {
        myScenario?.Dispose();
        myReplacementScenario?.Dispose();
    }

    [Given("delivery roles {string}")]
    public void GivenDeliveryRoles(string commaSeparatedRoles)
    {
        myRoles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myScenario = new BackendScenario(myWorkspace);
        myScenario.ConfigureRoles(myRoles);
    }

    [Given("{string} has an outbound note to {string}")]
    public void GivenRoleHasAnOutboundNoteTo(string role, string recipient)
    {
        var draftPath = myDrafts.WriteNoteDraft(role, recipient, "50", "Ready for review.");
        myWorkspace.RunRoleTool(role, "squad", ["handoff", draftPath]);
    }

    [Given("{string} already has the recipient copy")]
    public void GivenRoleAlreadyHasTheRecipientCopy(string role) =>
        myMailbox.SeedExistingRecipientCopy("coder", role, RecipientCopyMarker);

    [Given("{string} has existing new inbox work {string} from {string}")]
    public void GivenRoleHasExistingNewInboxWork(string role, string task, string sender) =>
        myTaskMailbox.QueueTask(role, sender, "50", task);

    [Given("{string} has existing in-process inbox work {string} from {string}")]
    public void GivenRoleHasExistingInProcessInboxWork(string role, string task, string sender) =>
        myTaskMailbox.PutTaskInProcess(role, sender, "50", task);

    [Given("the existing inbox work for {string} is recorded")]
    public void GivenTheExistingInboxWorkIsRecorded(string role) =>
        myWorkspace.Set(SnapshotKey, myMailbox.InboxContentSnapshot(role));

    [When("the squad host processes the handoff outbox")]
    public Task WhenTheSquadHostProcessesTheHandoffOutbox() => StartCurrentScenarioAsync(myScenario!);

    [When("the squad host starts")]
    public Task WhenTheSquadHostStarts() => StartCurrentScenarioAsync(myScenario!);

    [When("the squad host restarts with a replacement session")]
    public async Task WhenTheSquadHostRestartsWithAReplacementSession()
    {
        await myScenario!.ShutdownAsync();
        myReplacementScenario = new BackendScenario(myWorkspace);
        await StartCurrentScenarioAsync(myReplacementScenario);
    }

    private async Task StartCurrentScenarioAsync(BackendScenario scenario)
    {
        scenario.EnableFakeProviderControl();
        await scenario.StartAsync<FakeAgentProviderFactory>(continueLaunch: true);
        foreach (var role in myRoles)
        {
            await scenario.WaitForRoleSessionStartedAsync(role);
        }
        myCurrentScenario = scenario;
    }

    [Then("{string}'s recipient copy is unchanged")]
    public void ThenTheRecipientCopyIsUnchanged(string role) =>
        Assert.That(myMailbox.SingleNewInboxRawContent(role), Is.EqualTo(RecipientCopyMarker));

    [Then("the {string} agent observes a recovery wake-up message")]
    public async Task ThenTheAgentObservesARecoveryWakeUpMessage(string role) =>
        await myCurrentScenario!.Agent(role).WaitForHarnessMessageAsync(content => content == WakeUpMessage);

    [Then("the existing inbox work for {string} remains unchanged")]
    public void ThenTheExistingInboxWorkRemainsUnchanged(string role) =>
        Assert.That(myMailbox.InboxContentSnapshot(role), Is.EqualTo(myWorkspace.Get<IReadOnlyDictionary<string, string>>(SnapshotKey)));
}
