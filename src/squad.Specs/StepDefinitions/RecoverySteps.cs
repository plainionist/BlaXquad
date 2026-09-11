using squad.Specs.Support.Scenarios;
using squad.AgentProvider.Fake;
using squad.Specs.Support.Mailboxes;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives durable-delivery recovery scenarios exclusively across the real process/protocol boundary: a real
/// "squad handoff" queued into a role's own worktree, durable inbox fixtures seeded directly on disk for
/// otherwise-uncreatable "already delivered" or "pre-existing" prerequisites, Headquarters launched or restarted
/// against durable state through the scenario's own shared <see cref="BackendScenario"/> (and, for restart
/// scenarios, a second independently launched replacement host against the same workspace), and the recipient's
/// own fake session reporting wake-up harness messages across the fake-provider control pipe. Never constructs
/// <c>InProcessHandoffPoller</c>, <c>HandoffDeliveryService</c>, <c>IRoleNotifier</c>, or a role-row product type.
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
    private readonly BackendScenario myScenario;
    private readonly HandoffMailboxObserver myMailbox;
    private readonly TaskMailboxFixture myTaskMailbox;
    private BackendScenario? myCurrentScenario;

    public RecoverySteps(ScenarioWorkspace workspace, BackendScenario scenario, HandoffMailboxObserver mailbox, TaskMailboxFixture taskMailbox)
    {
        myWorkspace = workspace;
        myScenario = scenario;
        myMailbox = mailbox;
        myTaskMailbox = taskMailbox;
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

    [When("{string}'s session {word}")]
    public Task WhenRoleSSessionLifecycleEnds(string role, string lifecycle) => lifecycle switch
    {
        "stops" => myCurrentScenario!.Agent(role).CompleteSessionAsync(),
        "fails" => myCurrentScenario!.Agent(role).FailSessionAsync("simulated session failure"),
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, "Expected 'stops' or 'fails'."),
    };

    [When("the operator launches Headquarters, continuing from durable state")]
    public Task WhenTheOperatorLaunchesHeadquartersContinuingFromDurableState() => StartCurrentScenarioAsync(myScenario);

    [When("the operator restarts Headquarters, continuing from durable state")]
    public async Task WhenTheOperatorRestartsHeadquartersContinuingFromDurableState()
    {
        await myCurrentScenario!.ShutdownAsync();
        var replacement = myScenario.CreateReplacement();
        await StartCurrentScenarioAsync(replacement);
    }

    private async Task StartCurrentScenarioAsync(BackendScenario scenario)
    {
        scenario.EnableFakeProviderControl();
        await scenario.StartAsync<FakeAgentProviderFactory>(continueLaunch: true);
        foreach (var role in myScenario.ConfiguredRoles)
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
