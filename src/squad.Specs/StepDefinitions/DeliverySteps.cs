using global::squad.Specs.Support;
using global::squad.Agent.Configuration;
using global::squad.Core;
using System.Collections.Concurrent;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class DeliverySteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private readonly Dictionary<string, string> myWorktrees = new(StringComparer.Ordinal);
    private readonly List<RoleRow> myDeliveryRoles = [];
    private readonly RecordingRoleNotifier myNotifier = new();
    private readonly ConcurrentQueue<string> myDeliveryLog = [];

    public DeliverySteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("delivery roles {string}")]
    public void GivenDeliveryRoles(string commaSeparatedRoles)
    {
        var roles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myDeliveryRoles.Clear();
        foreach (var role in roles)
        {
            var worktree = myWorkspace.PathInWorkspace("worktrees", role);
            myWorktrees[role] = worktree;
            Directory.CreateDirectory(worktree);
            myDeliveryRoles.Add(new RoleRow(role, role, worktree, role, "task"));
        }
    }

    [Given("{string} has an outbound note to {string}")]
    public void GivenRoleHasAnOutboundNoteTo(string sender, string recipients)
    {
        var path = Path.Combine(
            myWorktrees[sender], ".blaxquad", "handoffs", "outbox",
            $"50_20260822T120000Z_000001_from_{sender}_to_{recipients.Replace(',', '_')}.handoff");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $"id: test-1\nfrom: {sender}\nto: {recipients}\npriority: 50\ntype: note\nmessage: Ready for review.\n\nReady for review.\n");
    }

    [Given("the recording notifier will fail")]
    public void GivenTheRecordingNotifierWillFail() => myNotifier.Fail = true;

    [Given("an empty project")]
    public void GivenAnEmptyProject()
    {
    }

    [Given("{string} already has the recipient copy")]
    public void GivenRoleAlreadyHasTheRecipientCopy(string role)
    {
        var source = Directory.GetFiles(
            Path.Combine(myWorktrees["coder"], ".blaxquad", "handoffs", "outbox"),
            "*.handoff").Single();
        var target = Path.Combine(
            myWorktrees[role], ".blaxquad", "handoffs", "inbox", "new", Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    [When("the squad host processes the handoff outbox")]
    public async Task WhenTheSquadHostProcessesTheHandoffOutbox()
    {
        await using var poller = new InProcessHandoffPoller(
            myDeliveryRoles,
            myNotifier,
            parts => myDeliveryLog.Enqueue(string.Join(" ", parts)));
        await poller.StartAsync();
        myWorkspace.WaitUntil(
            () => SentFiles().Length + FailedFiles().Length == 1,
            "the host to archive the outbound handoff");
        await poller.StopAsync();
    }

    [Then("the sender handoff is archived as sent")]
    public void ThenTheSenderHandoffIsArchivedAsSent() =>
        Assert.That(SentFiles(), Has.Length.EqualTo(1));

    [Then("the sender handoff is archived as failed")]
    public void ThenTheSenderHandoffIsArchivedAsFailed() =>
        Assert.That(FailedFiles(), Has.Length.EqualTo(1));

    [Then("{string} has one new handoff")]
    public void ThenRoleHasOneNewHandoff(string role) =>
        Assert.That(NewHandoffs(role), Has.Length.EqualTo(1));

    [Then("{string} has no new handoff")]
    public void ThenRoleHasNoNewHandoff(string role) =>
        Assert.That(NewHandoffs(role), Is.Empty);

    [Then("the new handoff for {string} has recipient header {string}")]
    public void ThenTheNewHandoffHasRecipientHeader(string role, string recipient)
    {
        Assert.That(File.ReadLines(NewHandoffs(role).Single()), Does.Contain($"recipient: {recipient}"));
    }

    [Then("a wake-up was recorded for {string}")]
    public void ThenAWakeUpWasRecordedFor(string role)
        => Assert.That(myNotifier.Notifications.Select(notification => notification.Recipient), Does.Contain(role));

    [Then("the wake-up names the installed ready command")]
    public void ThenTheWakeUpNamesTheInstalledReadyCommand()
        => Assert.That(myNotifier.Notifications.Select(notification => notification.Message), Has.Some.EqualTo(RecordingRoleNotifier.WakeMessage));

    [Then("no wake-up was recorded")]
    public void ThenNoWakeUpWasRecorded() =>
        Assert.That(myNotifier.Notifications, Is.Empty);

    [Then("the delivery log contains {string}")]
    public void ThenTheDeliveryLogContains(string expected) =>
        Assert.That(myDeliveryLog, Has.Some.Contains(expected));

    private string[] SentFiles() => ArchiveFiles("sent");
    private string[] FailedFiles() => ArchiveFiles("failed");

    private string[] ArchiveFiles(string state)
    {
        var directory = Path.Combine(myWorktrees["coder"], ".blaxquad", "handoffs", state);
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.handoff") : [];
    }

    private string[] NewHandoffs(string role)
    {
        var directory = Path.Combine(myWorktrees[role], ".blaxquad", "handoffs", "inbox", "new");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.handoff") : [];
    }

}



