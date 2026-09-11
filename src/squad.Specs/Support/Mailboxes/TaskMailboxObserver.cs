using squad.Specs.Support;

namespace squad.Specs.Support.Mailboxes;

/// <summary>
/// A test-owned semantic view over a role worktree's durable inbound task mailbox. Directory enumeration and
/// header inspection are confined here so step definitions only ask whether a named task is queued, in process, or
/// completed.
/// </summary>
public sealed class TaskMailboxObserver
{
    private readonly ScenarioWorkspace myWorkspace;

    public TaskMailboxObserver(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    internal bool IsTaskQueued(string role, string task) => Find(role, "new", task) is not null;

    internal bool IsTaskInProcess(string role, string task) => Find(role, "in_process", task) is not null;

    internal bool IsTaskCompleted(string role, string task) => Find(role, "completed", task) is not null;

    private string? Find(string role, string state, string task)
    {
        var directory = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox", state);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*.handoff", SearchOption.AllDirectories)
            .SingleOrDefault(path => File.ReadLines(path).Contains($"task: {task}"));
    }
}
