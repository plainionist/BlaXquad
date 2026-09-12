using squad.Domain;
using squad.Handoffs;
using squad.Specs.Support.Scenarios;
using squad.Specs.Support.Mailboxes;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HandoffSteps
{
    private const string PendingArgsKey = "handoffPendingArgs";
    private const string SenderRoleKey = "handoffSenderRole";
    private const string CommitKey = "commit";
    private readonly ScenarioWorkspace myWorkspace;
    private readonly HandoffMailboxObserver myMailbox;

    public HandoffSteps(ScenarioWorkspace workspace, HandoffMailboxObserver mailbox)
    {
        myWorkspace = workspace;
        myMailbox = mailbox;
    }

    [Given("{string} has a committed change")]
    public void GivenRoleHasACommittedChange(string role)
    {
        myWorkspace.WriteFileInRoleWorktree(role, $"{role}-change.txt", "completed\n");
        Assert.That(myWorkspace.RunRoleGit(role, "add", $"{role}-change.txt").ExitCode, Is.Zero);
        Assert.That(myWorkspace.RunRoleGit(role, "commit", "--quiet", "-m", $"{role} change").ExitCode, Is.Zero);
        var result = myWorkspace.RunRoleGit(role, "rev-parse", "--short=10", "HEAD");
        Assert.That(result.ExitCode, Is.Zero);
        myWorkspace.Set(CommitKey, result.StdOut.Trim());
    }

    [Given("{string} has an uncommitted change")]
    public void GivenRoleHasAnUncommittedChange(string role) =>
        myWorkspace.WriteFileInRoleWorktree(role, $"{role}-dirty-change.txt", "not committed\n");

    [Given("{string} prepares a Git handoff with task {string} to:")]
    [When("{string} prepares a Git handoff with task {string} to:")]
    public void GivenRolePreparesAGitHandoffToRecipients(string role, string task, Table recipients) =>
        PrepareCommit(role, priority: null, task, revision: null, recipients);

    [Given("{string} prepares a Git handoff with priority {string} and task {string} for revision {string} to:")]
    [When("{string} prepares a Git handoff with priority {string} and task {string} for revision {string} to:")]
    public void GivenRolePreparesAGitHandoffForRevisionToRecipients(string role, string priority, string task, string revision, Table recipients) =>
        PrepareCommit(role, priority, task, revision, recipients);

    [Given("{string} prepares a note with priority {string} and message {string} to:")]
    [When("{string} prepares a note with priority {string} and message {string} to:")]
    public void GivenRolePreparesANoteToRecipients(string role, string priority, string message, Table recipients) =>
        PrepareNote(role, priority, message, recipients);

    [When("{string} runs `squad handoff` with arguments:")]
    public void WhenRoleRunsSquadHandoffWithArguments(string role, Table table)
    {
        myWorkspace.Set(SenderRoleKey, role);
        var args = table.Rows.Select(row => row["arg"]).ToList();
        myWorkspace.RunRoleTool(role, "squad", ["handoff", .. args]);
    }

    [When("the {string} role agent runs `squad handoff` from its worktree")]
    public void WhenTheRoleAgentRunsSquadHandoffFromItsWorktree(string role) =>
        myWorkspace.RunRoleTool(role, "squad", ["handoff", .. myWorkspace.Get<List<string>>(PendingArgsKey)]);

    [Then("one handoff is queued")]
    public void ThenOneHandoffIsQueued() =>
        Assert.That(myMailbox.QueuedHandoffs(CurrentSender()), Has.Exactly(1).Items);

    [Then("no handoff is queued")]
    public void ThenNoHandoffIsQueued() =>
        Assert.That(myMailbox.QueuedHandoffs(CurrentSender()), Is.Empty);

    [Then("the queued handoff was sent by {string} to:")]
    public void ThenTheQueuedHandoffWasSentByToRecipients(string sender, Table recipients)
    {
        var handoff = SingleQueuedHandoff();
        Assert.Multiple(() =>
        {
            Assert.That(handoff.Sender, Is.EqualTo(new SquadMemberId(sender)));
            Assert.That(handoff.Recipients, Is.EqualTo(RecipientArray(recipients).Select(role => new SquadMemberId(role))));
        });
    }

    [Then("the queued handoff has priority {string}")]
    public void ThenTheQueuedHandoffHasPriority(string priority) =>
        Assert.That(SingleQueuedHandoff().Priority, Is.EqualTo(HandoffPriority.Parse(priority)));

    [Then("the queued handoff is a Git handoff for task {string}")]
    public void ThenTheQueuedHandoffIsAGitHandoffForTask(string task)
    {
        var handoff = SingleQueuedHandoff();
        Assert.Multiple(() =>
        {
            Assert.That(handoff.Type, Is.EqualTo("git_handoff"));
            Assert.That(handoff.Task, Is.EqualTo(task));
        });
    }

    [Then("the queued handoff instructs merging the committed change")]
    public void ThenTheQueuedHandoffInstructsMergingTheCommittedChange()
    {
        var handoff = SingleQueuedHandoff();
        Assert.That(
            handoff.InstructsMergingCommit(CurrentSender(), myWorkspace.Get<string>(CommitKey)),
            Is.True,
            () => $"Payload did not instruct merging the committed change: {handoff.Payload}");
    }

    [Then("the queued handoff is a note with message {string}")]
    public void ThenTheQueuedHandoffIsANoteWithMessage(string message)
    {
        var handoff = SingleQueuedHandoff();
        Assert.Multiple(() =>
        {
            Assert.That(handoff.Type, Is.EqualTo("note"));
            Assert.That(handoff.Message, Is.EqualTo(message));
            Assert.That(handoff.Payload, Is.EqualTo(message));
        });
    }

    private void PrepareCommit(string role, string? priority, string task, string? revision, Table recipients)
    {
        myWorkspace.Set(SenderRoleKey, role);
        var args = new List<string> { "commit", "--to", RecipientsFrom(recipients), "--task", task };
        if (priority is not null)
        {
            args.Add("--priority");
            args.Add(priority);
        }
        if (revision is not null)
        {
            args.Add("--commit");
            args.Add(revision);
        }
        myWorkspace.Set(PendingArgsKey, args);
    }

    private void PrepareNote(string role, string priority, string message, Table recipients)
    {
        myWorkspace.Set(SenderRoleKey, role);
        myWorkspace.Set(PendingArgsKey, new List<string> { "note", "--to", RecipientsFrom(recipients), "--priority", priority, "--message", message });
    }

    private QueuedHandoff SingleQueuedHandoff() => myMailbox.SingleQueuedHandoff(CurrentSender());

    private string CurrentSender() => myWorkspace.Get<string>(SenderRoleKey);

    private static string RecipientsFrom(Table recipients) => string.Join(",", RecipientArray(recipients));

    private static string[] RecipientArray(Table recipients) => recipients.Rows.Select(row => row["role"]).ToArray();
}
