namespace squad.Specs.Support;

/// <summary>
/// A test-owned semantic view over a role worktree's durable outbound handoff mailbox. Directory enumeration and
/// parsing of the on-disk handoff representation are confined to this observer so step definitions never inspect
/// headers, payload separators, file names, or extensions themselves.
/// </summary>
public sealed class HandoffMailboxObserver
{
    private readonly ScenarioWorkspace myWorkspace;

    public HandoffMailboxObserver(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    /// <summary>Every handoff currently queued in a role's outbox, in stable file order.</summary>
    public IReadOnlyList<QueuedHandoff> QueuedHandoffs(string senderRole)
    {
        var outbox = Path.Combine(myWorkspace.RoleWorktreePath(senderRole), ".blaxquad", "handoffs", "outbox");
        if (!Directory.Exists(outbox))
        {
            return [];
        }

        return Directory.GetFiles(outbox, "*.handoff", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(Parse)
            .ToList();
    }

    /// <summary>The one handoff expected to be queued in a role's outbox.</summary>
    public QueuedHandoff SingleQueuedHandoff(string senderRole) => QueuedHandoffs(senderRole).Single();

    private static QueuedHandoff Parse(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var parts = text.Split("\n\n", 2);
        var headers = parts[0]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(": ", 2))
            .ToDictionary(field => field[0], field => field[1], StringComparer.Ordinal);
        var payload = parts.Length > 1 ? parts[1].TrimEnd('\n') : "";

        return new QueuedHandoff(
            Sender: headers["from"],
            Recipients: headers["to"].Split(',', StringSplitOptions.RemoveEmptyEntries),
            Priority: headers["priority"],
            Type: headers["type"],
            Task: headers.GetValueOrDefault("task"),
            Message: headers.GetValueOrDefault("message"),
            Payload: payload);
    }
}

/// <summary>A queued handoff described in user terms: who sent it, who receives it, and its delivery instruction.</summary>
public sealed record QueuedHandoff(
    string Sender,
    IReadOnlyList<string> Recipients,
    string Priority,
    string Type,
    string? Task,
    string? Message,
    string Payload)
{
    /// <summary>Whether the payload instructs merging and processing the given committed change from the sender.</summary>
    public bool InstructsMergingCommit(string sender, string commit) =>
        Payload == $"merge_and_process {sender} {commit}";
}
