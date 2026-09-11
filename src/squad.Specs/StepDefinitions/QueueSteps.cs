using squad.Specs.Support;
using squad.Specs.Support.Mailboxes;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class QueueSteps
{
    private const string CurrentRoleKey = "queueCurrentRole";
    private readonly ScenarioWorkspace myWorkspace;
    private readonly TaskMailboxFixture myTaskMailbox;
    private readonly TaskMailboxObserver myTaskObserver;

    public QueueSteps(ScenarioWorkspace workspace, TaskMailboxFixture taskMailbox, TaskMailboxObserver taskObserver)
    {
        myWorkspace = workspace;
        myTaskMailbox = taskMailbox;
        myTaskObserver = taskObserver;
    }

    [Given("{string} has these queued tasks:")]
    [Given("{string} has this queued task:")]
    public void GivenRoleHasQueuedTasks(string role, DataTable tasks)
    {
        myWorkspace.Set(CurrentRoleKey, role);
        foreach (var row in tasks.Rows)
        {
            myTaskMailbox.QueueTask(role, row["from"], row["priority"], row["task"]);
        }
    }

    [Given("{string} is processing task {string} from {string}")]
    public void GivenRoleIsProcessingTask(string role, string task, string sender)
    {
        myWorkspace.Set(CurrentRoleKey, role);
        myTaskMailbox.PutTaskInProcess(role, sender, "10", task);
    }

    [Given("{string} is also processing task {string} from {string}")]
    public void GivenRoleIsAlsoProcessingTask(string role, string task, string sender)
    {
        myWorkspace.Set(CurrentRoleKey, role);
        myTaskMailbox.PutTaskInProcess(role, sender, "20", task);
    }

    [Given("{string} is processing this batch:")]
    public void GivenRoleIsProcessingThisBatch(string role, DataTable tasks)
    {
        myWorkspace.Set(CurrentRoleKey, role);
        myTaskMailbox.PutBatchInProcess(role, tasks.Rows.Select(row => (row["from"], row["priority"], row["task"])));
    }

    [Given("the completion archive already contains that task")]
    public void GivenTheCompletionArchiveAlreadyContainsThatTask() =>
        myTaskMailbox.DuplicateCurrentTaskIntoCompletedArchive(myWorkspace.Get<string>(CurrentRoleKey));

    [Given("the {string} role has an empty receive mode")]
    public void GivenTheRoleHasAnEmptyReceiveMode(string role) =>
        myWorkspace.ConfigureReceiveMode(role, "");

    [When("the {string} role agent runs `squad ready-for-next` from its worktree")]
    public void WhenTheRoleAgentRunsSquadReadyForNextFromItsWorktree(string role)
    {
        myWorkspace.Set(CurrentRoleKey, role);
        myWorkspace.RunRoleTool(role, "squad", ["ready-for-next"]);
    }

    [Given("a nested directory exists")]
    public void GivenANestedDirectoryExists() =>
        Directory.CreateDirectory(myWorkspace.PathInWorkspace("nested", "current"));

    [When("the nested directory runs `squad ready-for-next`")]
    public void WhenTheNestedDirectoryRunsSquadReadyForNext() =>
        myWorkspace.RunTool(
            "squad",
            ["ready-for-next"],
            workingDirectory: myWorkspace.PathInWorkspace("nested", "current"));

    [Given("a Git project with two roles sharing the current worktree")]
    public void GivenAGitProjectWithTwoRolesSharingTheCurrentWorktree() =>
        myWorkspace.ConfigureProjectWithRolesSharingWorktree("coder", "reviewer");

    [When("the ambiguous current worktree runs `squad ready-for-next`")]
    public void WhenTheAmbiguousCurrentWorktreeRunsSquadReadyForNext() =>
        myWorkspace.RunTool("squad", ["ready-for-next"]);

    [When("the {string} role agent runs `squad done-with-current` from its worktree")]
    public void WhenTheRoleAgentRunsSquadDoneWithCurrentFromItsWorktree(string role)
    {
        myWorkspace.Set(CurrentRoleKey, role);
        myWorkspace.RunRoleTool(role, "squad", ["done-with-current"]);
    }

    [Then("task {string} is in process")]
    public void ThenTaskIsInProcess(string task) =>
        Assert.That(myTaskObserver.IsTaskInProcess(CurrentRole(), task), Is.True);

    [Then("task {string} remains queued")]
    public void ThenTaskRemainsQueued(string task) =>
        Assert.That(myTaskObserver.IsTaskQueued(CurrentRole(), task), Is.True);

    [Then("task {string} is completed")]
    public void ThenTaskIsCompleted(string task) =>
        Assert.That(myTaskObserver.IsTaskCompleted(CurrentRole(), task), Is.True);

    private string CurrentRole() => myWorkspace.Get<string>(CurrentRoleKey);
}



