namespace squad.Specs.Support;

/// <summary>
/// Arranges durable inbound task state directly on disk for a role's worktree, for prerequisite queue state that
/// cannot be produced through a supported command (several competing queued tasks, an already in-process task, or
/// a pre-existing completion-archive collision). Confines the on-disk task file layout and serialization so step
/// definitions never construct or parse it directly.
/// </summary>
public sealed class TaskMailboxFixture
{
    private readonly ScenarioWorkspace myWorkspace;
    private int mySequence;

    public TaskMailboxFixture(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    /// <summary>Seeds a task waiting in the role's new-work queue.</summary>
    public void QueueTask(string role, string sender, string priority, string task) =>
        Write(role, "new", sender, priority, task);

    /// <summary>Seeds a task already accepted as the role's current in-process work.</summary>
    public void PutTaskInProcess(string role, string sender, string priority, string task) =>
        Write(role, "in_process", sender, priority, task);

    /// <summary>
    /// Copies the role's sole in-process task file into its completion archive, simulating a pre-existing archive
    /// collision without disturbing the current in-process task itself.
    /// </summary>
    public void DuplicateCurrentTaskIntoCompletedArchive(string role)
    {
        var inProcess = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox", "in_process");
        var source = Directory.GetFiles(inProcess, "*.handoff").Single();
        var target = Path.Combine(
            myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox", "completed", Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    private void Write(string role, string state, string sender, string priority, string task)
    {
        mySequence++;
        var filename = $"{priority}_20260822T120000Z_{mySequence:D6}_from_{sender}_to_{role}.handoff";
        var content =
            $"id: test-{mySequence}\nfrom: {sender}\nto: {role}\nrecipient: {role}\npriority: {priority}\ntype: git_handoff\ntask: {task}\ncommit: 0123456789\n\nmerge_and_process {sender} 0123456789\n";
        var path = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox", state, filename);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\n", Environment.NewLine));
    }
}
