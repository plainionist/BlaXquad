using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class QueueSteps
{
    private const string CurrentRoleKey = "queueCurrentRole";
    private readonly ScenarioWorkspace myWorkspace;
    private readonly TaskMailboxFixture myTaskMailbox;
    private readonly TaskMailboxObserver myTaskObserver;
    private int mySequence;

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
        myWorkspace.InitializeGitRepository();
        myWorkspace.WriteFile(
            "blaxquad/squad.json",
            $$"""
            {
              "roles": [
                { "name": "{{role}}", "worktree": "master", "receiveMode": "batch", "agent": {} }
              ]
            }
            """ + "\n");
        myWorkspace.RegisterRoleWorktree(role, myWorkspace.Root);
    }

    [Given("a Git project with role {string} and an empty receive mode")]
    public void GivenAGitProjectWithRoleAndAnEmptyReceiveMode(string role) =>
        myWorkspace.WriteFile(
            "blaxquad/squad.json",
            $$"""
            {
              "roles": [
                { "name": "{{role}}", "worktree": "{{role}}", "receiveMode": "", "agent": {} }
              ]
            }
            """ + "\n");

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
        var batchName = "batch_20260822T120000Z_000001";
        foreach (var row in tasks.Rows)
        {
            WriteBatchItem(row["from"], row["priority"], row["task"], batchName);
        }
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
        Directory.CreateDirectory(Path.Combine(myWorkspace.RoleWorktreePath("reviewer"), "nested", "current"));

    [When("the nested directory checks for work")]
    public void WhenTheNestedDirectoryChecksForWork() =>
        myWorkspace.RunTool(
            "squad",
            ["ready-for-next"],
            workingDirectory: Path.Combine(myWorkspace.RoleWorktreePath("reviewer"), "nested", "current"));

    [Given("a Git project with two roles sharing the current worktree")]
    public void GivenAGitProjectWithTwoRolesSharingTheCurrentWorktree()
    {
        myWorkspace.InitializeGitRepository();
        myWorkspace.WriteFile(
            "blaxquad/squad.json",
            """
            {
              "roles": [
                { "name": "coder", "worktree": "master", "agent": {} },
                { "name": "reviewer", "worktree": "master", "agent": {} }
              ]
            }
            """ + "\n");
    }

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

    private void WriteBatchItem(string sender, string priority, string task, string batchName)
    {
        mySequence++;
        var filename = $"{priority}_20260822T120000Z_{mySequence:D6}_from_{sender}_to_reviewer.handoff";
        myWorkspace.WriteFile(
            $".blaxquad/handoffs/inbox/in_process/{batchName}/{filename}",
            $"id: test-{mySequence}\nfrom: {sender}\nto: reviewer\nrecipient: reviewer\npriority: {priority}\ntype: git_handoff\ntask: {task}\ncommit: 0123456789\n\nmerge_and_process {sender} 0123456789\n");
    }
}



