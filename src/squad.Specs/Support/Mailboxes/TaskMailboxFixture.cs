using squad.Specs.Support.Scenarios;

namespace squad.Specs.Support.Mailboxes;

/// <summary>
/// Arranges durable inbound task and batch state directly on disk for a role's worktree, for prerequisite queue
/// state that cannot be produced through a supported command (several competing queued tasks, an already
/// in-process task or batch, or a pre-existing completion-archive collision). Confines the on-disk task file layout
/// and serialization so step definitions never construct or parse it directly.
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
    internal void QueueTask(string role, string sender, string priority, string task) =>
        Write(role, "new", sender, priority, task);

    /// <summary>Seeds a task already accepted as the role's current in-process work.</summary>
    internal void PutTaskInProcess(string role, string sender, string priority, string task) =>
        Write(role, "in_process", sender, priority, task);

    /// <summary>
    /// Seeds a batch of tasks already accepted as the role's current in-process work, sharing one batch folder so a
    /// batch role's "done-with-current" completes and inspects them together.
    /// </summary>
    internal void PutBatchInProcess(string role, IEnumerable<(string Sender, string Priority, string Task)> items)
    {
        mySequence++;
        var batchName = $"batch_20260822T120000Z_{mySequence:D6}";
        foreach (var item in items)
        {
            Write(role, "in_process", item.Sender, item.Priority, item.Task, batchName);
        }
    }

    /// <summary>
    /// Copies the role's sole in-process task file into its completion archive, simulating a pre-existing archive
    /// collision without disturbing the current in-process task itself.
    /// </summary>
    internal void DuplicateCurrentTaskIntoCompletedArchive(string role)
    {
        var inProcess = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox", "in_process");
        var source = Directory.GetFiles(inProcess, "*.handoff").Single();
        var target = Path.Combine(
            myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox", "completed", Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    private void Write(string role, string state, string sender, string priority, string task, string? batchName = null)
    {
        mySequence++;
        var filename = $"{priority}_20260822T120000Z_{mySequence:D6}_from_{sender}_to_{role}.handoff";
        var content =
            $"id: test-{mySequence}\nfrom: {sender}\nto: {role}\nrecipient: {role}\npriority: {priority}\ntype: git_handoff\ntask: {task}\ncommit: 0123456789\n\nmerge_and_process {sender} 0123456789\n";
        var stateDir = batchName is null ? state : Path.Combine(state, batchName);
        var path = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox", stateDir, filename);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\n", Environment.NewLine));
    }
}
