using System.Text.Json;
using squad.Handoffs;
using squad.Specs.Support.Scenarios;

namespace squad.Specs.Support.Mailboxes;

/// <summary>
/// A test-owned semantic view over a role worktree's durable inbound task mailbox. Directory enumeration and JSON
/// inspection are confined here so step definitions only ask whether a named task is queued, in process, or
/// completed.
/// </summary>
public sealed class TaskMailboxObserver
{
    private const string FileSuffix = ".handoff.json";

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
        var directory = InboxDir(HandoffQueue.Root(myWorkspace.RoleWorktreePath(role)), state);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*" + FileSuffix, SearchOption.AllDirectories)
            .SingleOrDefault(path => HasTask(path, task));
    }

    private static string InboxDir(string root, string state) => state switch
    {
        "new" => HandoffQueue.NewInbox(root),
        "in_process" => HandoffQueue.InProcessInbox(root),
        "completed" => HandoffQueue.CompletedInbox(root),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown inbox state"),
    };

    private static bool HasTask(string path, string task)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("gitHandoff", out var gitHandoff)
            && gitHandoff.ValueKind == JsonValueKind.Object
            && gitHandoff.TryGetProperty("task", out var taskValue)
            && taskValue.ValueKind == JsonValueKind.String
            && taskValue.GetString() == task;
    }
}
