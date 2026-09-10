using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HandoffSteps
{
    private const string DraftPathKey = "handoffDraftPath";
    private const string SenderRoleKey = "handoffSenderRole";
    private const string CommitKey = "commit";
    private readonly ScenarioWorkspace myWorkspace;
    private readonly HandoffDraftWriter myDrafts;
    private readonly HandoffMailboxObserver myMailbox;

    public HandoffSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
        myDrafts = new HandoffDraftWriter(workspace);
        myMailbox = new HandoffMailboxObserver(workspace);
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

    [Given("{string} prepares a Git handoff with priority {string} and task {string} to:")]
    public void GivenRolePreparesAGitHandoffToRecipients(string role, string priority, string task, Table recipients)
    {
        myWorkspace.Set(SenderRoleKey, role);
        myWorkspace.Set(
            DraftPathKey,
            myDrafts.WriteGitHandoffDraft(role, RecipientsFrom(recipients), priority, task, myWorkspace.Get<string>(CommitKey)));
    }

    [Given("{string} prepares a note to {string} with priority {string} and message {string}")]
    [When("{string} prepares a note to {string} with priority {string} and message {string}")]
    public void GivenRolePreparesANote(string role, string recipients, string priority, string message)
    {
        myWorkspace.Set(SenderRoleKey, role);
        myWorkspace.Set(DraftPathKey, myDrafts.WriteNoteDraft(role, recipients, priority, message));
    }

    [Given("{string} prepares a note with priority {string} and message {string} to:")]
    public void GivenRolePreparesANoteToRecipients(string role, string priority, string message, Table recipients)
    {
        myWorkspace.Set(SenderRoleKey, role);
        myWorkspace.Set(DraftPathKey, myDrafts.WriteNoteDraft(role, RecipientsFrom(recipients), priority, message));
    }

    [Given("{string} prepares this handoff draft:")]
    public void GivenRolePreparesThisHandoffDraft(string role, string draft)
    {
        myWorkspace.Set(SenderRoleKey, role);
        myWorkspace.Set(DraftPathKey, myDrafts.WriteRawDraft(role, draft));
    }

    [When("the {string} role agent runs `squad handoff` from its worktree")]
    public void WhenTheRoleAgentRunsSquadHandoffFromItsWorktree(string role) =>
        myWorkspace.RunRoleTool(role, "squad", ["handoff", myWorkspace.Get<string>(DraftPathKey)]);

    [Then("the draft is removed")]
    public void ThenTheDraftIsRemoved() =>
        Assert.That(File.Exists(myWorkspace.Get<string>(DraftPathKey)), Is.False);

    [Then("the draft remains")]
    public void ThenTheDraftRemains() =>
        Assert.That(File.Exists(myWorkspace.Get<string>(DraftPathKey)), Is.True);

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
            Assert.That(handoff.Sender, Is.EqualTo(sender));
            Assert.That(handoff.Recipients, Is.EqualTo(RecipientArray(recipients)));
        });
    }

    [Then("the queued handoff has priority {string}")]
    public void ThenTheQueuedHandoffHasPriority(string priority) =>
        Assert.That(SingleQueuedHandoff().Priority, Is.EqualTo(priority));

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

    private QueuedHandoff SingleQueuedHandoff() => myMailbox.SingleQueuedHandoff(CurrentSender());

    private string CurrentSender() => myWorkspace.Get<string>(SenderRoleKey);

    private static string RecipientsFrom(Table recipients) => string.Join(",", RecipientArray(recipients));

    private static string[] RecipientArray(Table recipients) => recipients.Rows.Select(row => row["role"]).ToArray();
}
