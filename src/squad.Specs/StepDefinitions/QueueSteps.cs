using global::squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class QueueSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private int mySequence;

    public QueueSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a Git project with task role {string}")]
    public void GivenAGitProjectWithTaskRole(string role) => CreateQueueProject(role, "task");

    [Given("a Git project with batch role {string}")]
    public void GivenAGitProjectWithBatchRole(string role) => CreateQueueProject(role, "batch");

    [Given("a Git project with role {string} and an empty receive mode")]
    public void GivenAGitProjectWithRoleAndAnEmptyReceiveMode(string role) => CreateQueueProject(role, "");

    [Given("{string} has these queued tasks:")]
    [Given("{string} has this queued task:")]
    public void GivenRoleHasQueuedTasks(string role, DataTable tasks)
    {
        foreach (var row in tasks.Rows)
            WriteTask("new", row["from"], row["priority"], row["task"]);
    }

    [Given("{string} is processing task {string} from {string}")]
    public void GivenRoleIsProcessingTask(string role, string task, string sender) =>
        WriteTask("in_process", sender, "10", task);

    [Given("{string} is also processing task {string} from {string}")]
    public void GivenRoleIsAlsoProcessingTask(string role, string task, string sender) =>
        WriteTask("in_process", sender, "20", task);

    [Given("{string} is processing this batch:")]
    public void GivenRoleIsProcessingThisBatch(string role, DataTable tasks)
    {
        var batchName = "batch_20260822T120000Z_000001";
        foreach (var row in tasks.Rows)
            WriteTask($"in_process/{batchName}", row["from"], row["priority"], row["task"]);
    }

    [Given("the completion archive already contains that task")]
    public void GivenTheCompletionArchiveAlreadyContainsThatTask()
    {
        var inProcess = myWorkspace.PathInWorkspace(".blaxquad", "handoffs", "inbox", "in_process");
        var source = Directory.GetFiles(inProcess, "*.handoff").Single();
        var target = myWorkspace.PathInWorkspace(
            ".blaxquad", "handoffs", "inbox", "completed", Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    [When("{string} checks for work")]
    public void WhenRoleChecksForWork(string role) => RunForRole("ready_for_next", role);

    [Given("a nested directory exists")]
    public void GivenANestedDirectoryExists() =>
        Directory.CreateDirectory(myWorkspace.PathInWorkspace("nested", "current"));

    [When("the nested directory checks for work")]
    public void WhenTheNestedDirectoryChecksForWork() =>
        myWorkspace.RunTool("squad", ["ready-for-next"], workingDirectory: myWorkspace.PathInWorkspace("nested", "current"));

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
    public void WhenRoleCompletesTheCurrentWork(string role) => RunForRole("done_with_current", role);

    [Then("task {string} is in process")]
    public void ThenTaskIsInProcess(string task) =>
        Assert.That(FindTask("in_process", task), Is.Not.Null);

    [Then("task {string} remains queued")]
    public void ThenTaskRemainsQueued(string task) =>
        Assert.That(FindTask("new", task), Is.Not.Null);

    [Then("task {string} is completed")]
    public void ThenTaskIsCompleted(string task) =>
        Assert.That(FindTask("completed", task), Is.Not.Null);

    private void CreateQueueProject(string role, string receiveMode)
    {
        myWorkspace.InitializeGitRepository();
        myWorkspace.WriteFile(
            "blaxquad/squad.json",
            $$"""
            {
              "roles": [
                { "name": "{{role}}", "worktree": "master", "receiveMode": "{{receiveMode}}", "agent": {} }
              ]
            }
            """ + "\n");
    }

    private void RunForRole(string script, string role)
    {
        myWorkspace.RunTool("squad", [script.Replace('_', '-')]);
    }

    private void WriteTask(string state, string sender, string priority, string task)
    {
        mySequence++;
        var filename = $"{priority}_20260822T120000Z_{mySequence:D6}_from_{sender}_to_reviewer.handoff";
        myWorkspace.WriteFile(
            $".blaxquad/handoffs/inbox/{state}/{filename}",
            $"id: test-{mySequence}\nfrom: {sender}\nto: reviewer\nrecipient: reviewer\npriority: {priority}\ntype: git_handoff\ntask: {task}\ncommit: 0123456789\n\nmerge_and_process {sender} 0123456789\n");
    }

    private string? FindTask(string state, string task)
    {
        var directory = myWorkspace.PathInWorkspace(".blaxquad", "handoffs", "inbox", state);
        if (!Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, "*.handoff", SearchOption.AllDirectories)
            .SingleOrDefault(path => File.ReadLines(path).Contains($"task: {task}"));
    }
}



