using global::squad.Configuration;

namespace squad.Handoffs;

sealed class HandoffDeliveryService
{
    private static readonly string[] myPreferredHeaderOrder =
    [
        "id", "from", "to", "recipient", "priority", "type", "role", "commit",
        "message", "created_at", "enqueued_at", "dequeued_at", "completed_at",
    ];

    private readonly IRoleNotifier myNotifier;
    private readonly Action<string[]> myLog;

    public HandoffDeliveryService(IRoleNotifier notifier, Action<string[]> log)
    {
        myNotifier = notifier;
        myLog = log;
    }

    public async Task ProcessOnceAsync(IReadOnlyList<RoleRow> roles, Func<bool>? stopRequested = null, CancellationToken cancellationToken = default)
    {
        var roleMap = roles.ToDictionary(r => r.Role);
        foreach (var (roleName, roleInfo) in roleMap)
        {
            var outboxDir = Path.Combine(roleInfo.WorktreePath, ".blaxquad", "handoffs", "outbox");
            foreach (var path in HandoffQueue.HandoffFiles(outboxDir))
            {
                if (stopRequested?.Invoke() == true)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await DeliverAsync(roleMap, roleName, path, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    myLog(["error", path, exception.Message]);
                    try
                    {
                        Fail(path, exception.Message);
                    }
                    catch (Exception nested)
                    {
                        myLog(["failed-to-archive", path, nested.Message]);
                    }
                }
            }
        }
    }

    public async Task RecoverAsync(IReadOnlyList<RoleRow> roles, CancellationToken cancellationToken = default)
    {
        foreach (var role in roles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasPendingInbox(role.WorktreePath))
                continue;
            try
            {
                await myNotifier.NotifyAsync(role.Role, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                myLog(["notify-failed", role.Role, exception.Message]);
            }
        }
    }

    private async Task DeliverAsync(Dictionary<string, RoleRow> roles, string senderRole, string path, CancellationToken cancellationToken)
    {
        var filename = Path.GetFileName(path);
        var (headers, body) = ParseMessage(path);
        if (!headers.TryGetValue("to", out var to) || string.IsNullOrEmpty(to))
        {
            Fail(path, "missing to header");
            return;
        }

        var recipients = SplitRecipients(to);
        if (recipients.Length == 0)
        {
            Fail(path, "missing to header");
            return;
        }

        var deliveries = new List<(string Recipient, RoleRow RoleInfo)>();
        foreach (var recipient in recipients)
        {
            if (!roles.TryGetValue(recipient, out var roleInfo))
                throw new InvalidOperationException($"unknown recipient {recipient}");
            deliveries.Add((recipient, roleInfo));
        }

        foreach (var (recipient, roleInfo) in deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(roleInfo.WorktreePath, ".blaxquad", "handoffs", "inbox", "new", filename);
            var deliveredHeaders = new Dictionary<string, string>(headers)
            {
                ["recipient"] = recipient,
                ["enqueued_at"] = Timestamps.Now(),
            };
            WriteRecipientArtifact(target, RenderMessage(deliveredHeaders, body));
        }

        var sentDir = Path.Combine(roles[senderRole].WorktreePath, ".blaxquad", "handoffs", "sent");
        MoveWithCollision(path, sentDir);
        myLog(["delivered", path]);

        foreach (var (_, roleInfo) in deliveries)
        {
            try
            {
                await myNotifier.NotifyAsync(roleInfo.Role, cancellationToken);
            }
            catch (Exception exception)
            {
                myLog(["notify-failed", roleInfo.Role, exception.Message]);
            }
        }
    }

    private static bool HasPendingInbox(string worktreePath)
    {
        var handoffs = Path.Combine(worktreePath, ".blaxquad", "handoffs", "inbox");
        return new[] { "new", "in_process" }.Any(state =>
        {
            var directory = Path.Combine(handoffs, state);
            return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.handoff", SearchOption.TopDirectoryOnly).Any();
        });
    }

    private static (Dictionary<string, string> Headers, string Body) ParseMessage(string path)
    {
        var (header, body) = HandoffHeaders.SplitMessage(File.ReadAllText(path));
        var headers = new Dictionary<string, string>();
        foreach (var rawLine in header.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var parts = line.Split(": ", 2);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                headers[parts[0]] = parts[1];
        }
        return (headers, body);
    }

    private static string[] SplitRecipients(string to)
    {
        var recipients = to.Split(',');
        var end = recipients.Length;
        while (end > 0 && recipients[end - 1].Length == 0)
            end--;
        return recipients[..end];
    }

    private static string RenderMessage(Dictionary<string, string> headers, string body)
    {
        var remaining = headers.Keys.Except(myPreferredHeaderOrder).OrderBy(k => k, StringComparer.Ordinal);
        var lines = myPreferredHeaderOrder.Concat(remaining).Where(headers.ContainsKey).Select(k => $"{k}: {headers[k]}");
        return string.Join("\n", lines) + "\n\n" + body;
    }

    private static void WriteRecipientArtifact(string target, string content)
    {
        if (Path.Exists(target))
            return;
        var targetDir = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(targetDir);
        var tmp = Path.Combine(targetDir, $".inbox.{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, target);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    private static void MoveWithCollision(string source, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileName(source);
        var target = Path.Combine(targetDir, baseName);
        if (Path.Exists(target))
            target = Path.Combine(targetDir, $"{Timestamps.Now()}_{baseName}");
        File.Move(source, target);
    }

    private void Fail(string path, string reason)
    {
        var handoffsDir = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
        var failedDir = Path.Combine(handoffsDir, "failed");
        myLog(["failed", path, reason]);
        File.WriteAllText(path + ".error", reason + "\n");
        MoveWithCollision(path, failedDir);
    }
}



