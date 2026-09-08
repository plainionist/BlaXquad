using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class QueueSteps
{
    private const string CurrentRoleKey = "queueCurrentRole";
    private readonly ScenarioWorkspace myWorkspace;
    private readonly TaskMailboxFixture myTaskMailbox;
    private readonly TaskMailboxObserver myTaskObserver;

    public QueueSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
        myTaskMailbox = new TaskMailboxFixture(workspace);
        myTaskObserver = new TaskMailboxObserver(workspace);
    }

    [Given("a Git project with task role {string}")]
    public void GivenAGitProjectWithTaskRole(string role) => myWorkspace.ConfigureProject(role);

    [Given("a Git project with batch role {string}")]
    public void GivenAGitProjectWithBatchRole(string role)
    {
        myWorkspace.ConfigureProject(role);
        myWorkspace.SetRoleReceiveMode(role, "batch");
    }

    [Given("a Git project with role {string} and an empty receive mode")]
    public void GivenAGitProjectWithRoleAndAnEmptyReceiveMode(string role) =>
        myWorkspace.SetRoleReceiveMode(role, "");

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

    [When("{string} checks for work")]
    public void WhenRoleChecksForWork(string role)
    {
        myWorkspace.Set(CurrentRoleKey, role);
        myWorkspace.RunRoleTool(role, "squad", ["ready-for-next"]);
    }

    [Given("a nested directory exists")]
    public void GivenANestedDirectoryExists() =>
        Directory.CreateDirectory(myWorkspace.PathInWorkspace("nested", "current"));

    [When("the nested directory checks for work")]
    public void WhenTheNestedDirectoryChecksForWork() =>
        myWorkspace.RunTool(
            "squad",
            ["ready-for-next"],
            workingDirectory: myWorkspace.PathInWorkspace("nested", "current"));

    [Given("a Git project with two roles sharing the current worktree")]
    public void GivenAGitProjectWithTwoRolesSharingTheCurrentWorktree() =>
        myWorkspace.ConfigureProjectWithRolesSharingWorktree("coder", "reviewer");

    [When("the ambiguous current worktree checks for work")]
    public void WhenTheAmbiguousCurrentWorktreeChecksForWork() =>
        myWorkspace.RunTool("squad", ["ready-for-next"]);

    [When("{string} completes the current work")]
    public void WhenRoleCompletesTheCurrentWork(string role)
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



