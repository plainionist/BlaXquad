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
    public IReadOnlyList<QueuedHandoff> QueuedHandoffs(string senderRole) =>
        ListHandoffs(senderRole, "outbox");

    /// <summary>The one handoff expected to be queued in a role's outbox.</summary>
    public QueuedHandoff SingleQueuedHandoff(string senderRole) => QueuedHandoffs(senderRole).Single();

    /// <summary>Every handoff a role's outbox has archived as durably sent, in stable file order.</summary>
    public IReadOnlyList<QueuedHandoff> SentHandoffs(string senderRole) => ListHandoffs(senderRole, "sent");

    /// <summary>Every handoff a role's outbox has archived as failed, in stable file order.</summary>
    public IReadOnlyList<QueuedHandoff> FailedHandoffs(string senderRole) => ListHandoffs(senderRole, "failed");

    /// <summary>Every handoff durably delivered into a role's new-inbox bucket, in stable file order.</summary>
    public IReadOnlyList<QueuedHandoff> NewInboxHandoffs(string recipientRole) =>
        ListHandoffs(recipientRole, Path.Combine("inbox", "new"));

    /// <summary>
    /// Seeds a raw outbound handoff artifact directly into a role's outbox, bypassing the "squad handoff" CLI's
    /// own recipient and field validation - the only way a scenario can arrange an otherwise-uncreatable durable
    /// prerequisite, such as fan-out naming an unconfigured recipient.
    /// </summary>
    public void SeedInvalidOutboundNote(string senderRole, string recipients, string message)
    {
        var outbox = Path.Combine(myWorkspace.RoleWorktreePath(senderRole), ".blaxquad", "handoffs", "outbox");
        Directory.CreateDirectory(outbox);
        var recipientSlug = recipients.Replace(',', '_');
        var path = Path.Combine(outbox, $"50_{Guid.NewGuid():N}_from_{senderRole}_to_{recipientSlug}.handoff");
        File.WriteAllText(
            path,
            $"id: seed-{Guid.NewGuid():N}\nfrom: {senderRole}\nto: {recipients}\npriority: 50\ntype: note\nmessage: {message}\n\n{message}\n");
    }

    /// <summary>
    /// Seeds a durable recipient inbox copy under the exact file name of a role's sole queued outbound handoff -
    /// the identity production delivery reuses unmodified for the recipient's artifact - with test-owned marker
    /// content that a fresh delivery would never render. Lets a scenario prove a retried delivery preserves this
    /// pre-existing "already persisted" copy byte-for-byte instead of overwriting it.
    /// </summary>
    public void SeedExistingRecipientCopy(string senderRole, string recipientRole, string markerContent)
    {
        var fileName = SingleQueuedHandoffFileName(senderRole);
        var target = Path.Combine(myWorkspace.RoleWorktreePath(recipientRole), ".blaxquad", "handoffs", "inbox", "new", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, markerContent);
    }

    /// <summary>The raw content of the sole handoff durably delivered into a role's new-inbox bucket, unparsed -
    /// so a scenario can assert it remains byte-for-byte identical to a previously seeded marker.</summary>
    public string SingleNewInboxRawContent(string recipientRole)
    {
        var newDir = Path.Combine(myWorkspace.RoleWorktreePath(recipientRole), ".blaxquad", "handoffs", "inbox", "new");
        return File.ReadAllText(Directory.GetFiles(newDir, "*.handoff", SearchOption.TopDirectoryOnly).Single());
    }

    /// <summary>
    /// Raw content of every durable artifact across a role's "new" and "in_process" inbox state directories, keyed
    /// by file name, so a scenario can assert recovery preserves entries exactly rather than merely checking
    /// counts.
    /// </summary>
    public IReadOnlyDictionary<string, string> InboxContentSnapshot(string role)
    {
        var handoffs = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", "inbox");
        return new[] { "new", "in_process" }
            .SelectMany(state =>
            {
                var directory = Path.Combine(handoffs, state);
                return Directory.Exists(directory)
                    ? Directory.GetFiles(directory, "*.handoff", SearchOption.TopDirectoryOnly)
                    : [];
            })
            .ToDictionary(path => Path.Combine(Path.GetFileName(Path.GetDirectoryName(path)!), Path.GetFileName(path)), File.ReadAllText, StringComparer.Ordinal);
    }

    /// <summary>The file name of the sole handoff currently queued in a role's outbox.</summary>
    private string SingleQueuedHandoffFileName(string senderRole)
    {
        var outbox = Path.Combine(myWorkspace.RoleWorktreePath(senderRole), ".blaxquad", "handoffs", "outbox");
        return Path.GetFileName(Directory.GetFiles(outbox, "*.handoff", SearchOption.TopDirectoryOnly).Single());
    }

    private IReadOnlyList<QueuedHandoff> ListHandoffs(string role, string relativeDirectory)
    {
        var directory = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "*.handoff", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(Parse)
            .ToList();
    }

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
            Payload: payload,
            Recipient: headers.GetValueOrDefault("recipient"));
    }
}

/// <summary>A queued handoff described in user terms: who sent it, who receives it, and its delivery instruction.
/// <paramref name="Recipient"/> is the single recipient header a durably delivered inbox copy carries - null for an
/// outbox, sent, or failed artifact, which still names every fan-out recipient only through <paramref name="Recipients"/>.</summary>
public sealed record QueuedHandoff(
    string Sender,
    IReadOnlyList<string> Recipients,
    string Priority,
    string Type,
    string? Task,
    string? Message,
    string Payload,
    string? Recipient = null)
{
    /// <summary>Whether the payload instructs merging and processing the given committed change from the sender.</summary>
    public bool InstructsMergingCommit(string sender, string commit) =>
        Payload == $"merge_and_process {sender} {commit}";
}
