using squad.Handoffs;
using squad.Specs.Support.Scenarios;

namespace squad.Specs.Support.Mailboxes;

/// <summary>
/// Arranges durable inbound task and batch state directly on disk for a role's worktree, for prerequisite queue
/// state that cannot be produced through a supported command (several competing queued tasks, an already
/// in-process task or batch, or a pre-existing completion-archive collision). Confines the on-disk task file layout
/// and JSON serialization so step definitions never construct or parse it directly. Writes plain JSON text
/// independently of production's <c>squad.Handoffs</c> model so a scenario never depends on production
/// serialization internals.
/// </summary>
public sealed class TaskMailboxFixture
{
    private const string FileSuffix = ".handoff.json";

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
        var root = HandoffQueue.Root(myWorkspace.RoleWorktreePath(role));
        var inProcess = HandoffQueue.InProcessInbox(root);
        var source = Directory.GetFiles(inProcess, "*" + FileSuffix).Single();
        var target = Path.Combine(HandoffQueue.CompletedInbox(root), Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    private void Write(string role, string state, string sender, string priority, string task, string? batchName = null)
    {
        mySequence++;
        var filename = $"{priority}_20260822T120000Z_{mySequence:D6}_from_{sender}_to_{role}{FileSuffix}";
        var content =
            $$"""
            {
              "id": "test-{{mySequence}}",
              "from": "{{sender}}",
              "to": ["{{role}}"],
              "recipient": "{{role}}",
              "priority": {{int.Parse(priority)}},
              "kind": "git_handoff",
              "gitHandoff": { "task": "{{task}}", "commit": "0123456789" },
              "createdAt": "2026-08-22T12:00:00Z",
              "enqueuedAt": "2026-08-22T12:00:00Z"
            }

            """;
        var root = HandoffQueue.Root(myWorkspace.RoleWorktreePath(role));
        var stateDir = InboxDir(root, state);
        var path = batchName is null ? Path.Combine(stateDir, filename) : Path.Combine(stateDir, batchName, filename);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string InboxDir(string root, string state) => state switch
    {
        "new" => HandoffQueue.NewInbox(root),
        "in_process" => HandoffQueue.InProcessInbox(root),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown inbox state"),
    };
}
