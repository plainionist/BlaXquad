using System.Text.Json;
using squad.Specs.Support.Scenarios;

namespace squad.Specs.Support.Mailboxes;

/// <summary>
/// A test-owned semantic view over a role worktree's durable outbound handoff mailbox. Directory enumeration and
/// parsing of the on-disk JSON handoff representation are confined to this observer so step definitions never
/// inspect JSON properties, file names, or extensions themselves. Parses independently of production's
/// <c>squad.Handoffs</c> model so a scenario never asserts production serializer formatting, property order, or
/// whitespace - only the documented, supported schema.
/// </summary>
public sealed class HandoffMailboxObserver
{
    private const string FileSuffix = ".handoff.json";

    private readonly ScenarioWorkspace myWorkspace;

    public HandoffMailboxObserver(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    /// <summary>Every handoff currently queued in a role's outbox, in stable file order.</summary>
    internal IReadOnlyList<QueuedHandoff> QueuedHandoffs(string senderRole) =>
        ListHandoffs(senderRole, "outbox");

    /// <summary>The one handoff expected to be queued in a role's outbox.</summary>
    internal QueuedHandoff SingleQueuedHandoff(string senderRole) => QueuedHandoffs(senderRole).Single();

    /// <summary>Every handoff a role's outbox has archived as durably sent, in stable file order.</summary>
    internal IReadOnlyList<QueuedHandoff> SentHandoffs(string senderRole) => ListHandoffs(senderRole, "sent");

    /// <summary>Every handoff a role's outbox has archived as failed, in stable file order.</summary>
    internal IReadOnlyList<QueuedHandoff> FailedHandoffs(string senderRole) => ListHandoffs(senderRole, "failed");

    /// <summary>The number of artifacts a role's outbox has archived as failed, counted without parsing them -
    /// an artifact archived as failed may itself be malformed content that could never be parsed.</summary>
    internal int FailedHandoffCount(string senderRole) => CountFiles(senderRole, "failed");

    /// <summary>Every handoff durably delivered into a role's new-inbox bucket, in stable file order.</summary>
    internal IReadOnlyList<QueuedHandoff> NewInboxHandoffs(string recipientRole) =>
        ListHandoffs(recipientRole, Path.Combine("inbox", "new"));

    /// <summary>Every handoff currently claimed into a role's in-process inbox bucket, in stable file order.</summary>
    internal IReadOnlyList<QueuedHandoff> InProcessInboxHandoffs(string recipientRole) =>
        ListHandoffs(recipientRole, Path.Combine("inbox", "in_process"));

    /// <summary>Every handoff archived into a role's completed-inbox bucket, in stable file order.</summary>
    internal IReadOnlyList<QueuedHandoff> CompletedInboxHandoffs(string recipientRole) =>
        ListHandoffs(recipientRole, Path.Combine("inbox", "completed"));

    /// <summary>
    /// Seeds a raw outbound handoff artifact directly into a role's outbox, bypassing the "squad handoff" CLI's
    /// own recipient and field validation - the only way a scenario can arrange an otherwise-uncreatable durable
    /// prerequisite, such as fan-out naming an unconfigured recipient.
    /// </summary>
    internal void SeedInvalidOutboundNote(string senderRole, string recipients, string message)
    {
        var outbox = Path.Combine(myWorkspace.RoleWorktreePath(senderRole), ".blaxquad", "handoffs", "outbox");
        Directory.CreateDirectory(outbox);
        var recipientSlug = recipients.Replace(',', '_');
        var path = Path.Combine(outbox, $"50_{Guid.NewGuid():N}_from_{senderRole}_to_{recipientSlug}{FileSuffix}");
        var recipientArray = string.Join(",", recipients.Split(',').Select(r => $"\"{r}\""));
        File.WriteAllText(
            path,
            $$"""
            {
              "id": "seed-{{Guid.NewGuid():N}}",
              "from": "{{senderRole}}",
              "to": [{{recipientArray}}],
              "recipient": null,
              "priority": 50,
              "kind": "note",
              "note": { "message": "{{message}}" },
              "createdAt": "2026-08-22T12:00:00Z"
            }
            """);
    }

    /// <summary>
    /// Seeds a raw, scenario-authored outbound handoff artifact - malformed JSON or a handoff kind whose variant
    /// data does not match - directly into a role's outbox, the only way a scenario can arrange a durable artifact
    /// production's own writer would never itself produce.
    /// </summary>
    internal void SeedInvalidOutboundContent(string senderRole, string content)
    {
        var outbox = Path.Combine(myWorkspace.RoleWorktreePath(senderRole), ".blaxquad", "handoffs", "outbox");
        Directory.CreateDirectory(outbox);
        var path = Path.Combine(outbox, $"50_{Guid.NewGuid():N}_from_{senderRole}_to_reviewer{FileSuffix}");
        File.WriteAllText(path, content);
    }

    private IReadOnlyList<QueuedHandoff> ListHandoffs(string role, string relativeDirectory)
    {
        var directory = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "*" + FileSuffix, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(Parse)
            .ToList();
    }

    private int CountFiles(string role, string relativeDirectory)
    {
        var directory = Path.Combine(myWorkspace.RoleWorktreePath(role), ".blaxquad", "handoffs", relativeDirectory);
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*" + FileSuffix, SearchOption.TopDirectoryOnly).Length
            : 0;
    }

    private static QueuedHandoff Parse(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var from = root.GetProperty("from").GetString()!;
        var kind = root.GetProperty("kind").GetString()!;
        var priority = root.GetProperty("priority").GetInt32();
        var task = GetOptionalString(root, "gitHandoff", "task");
        var commit = GetOptionalString(root, "gitHandoff", "commit");
        var message = GetOptionalString(root, "note", "message");
        var payload = kind == "git_handoff" ? $"merge_and_process {from} {commit}" : message ?? "";

        return new QueuedHandoff(
            Sender: from,
            Recipients: root.GetProperty("to").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            Priority: priority.ToString("D2"),
            Type: kind,
            Task: task,
            Message: message,
            Payload: payload,
            Recipient: GetOptionalString(root, "recipient"),
            CreatedAt: root.GetProperty("createdAt").GetString()!,
            EnqueuedAt: GetOptionalString(root, "enqueuedAt"),
            DequeuedAt: GetOptionalString(root, "dequeuedAt"),
            CompletedAt: GetOptionalString(root, "completedAt"));
    }

    private static string? GetOptionalString(JsonElement root, string objectProperty, string stringProperty) =>
        root.TryGetProperty(objectProperty, out var obj) && obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(stringProperty, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetOptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
