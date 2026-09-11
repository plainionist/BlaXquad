using squad.Configuration;

namespace squad.Handoffs.Delivery;

/// <summary>
/// Moves outbound handoff files atomically into recipient inboxes, archives sender artifacts, and wakes recipients.
/// Per-file delivery failures are archived without stopping the scan.
/// </summary>
sealed class HandoffDeliveryService
{
    private readonly IRoleNotifier myNotifier;
    private readonly HandoffDeliveryLog myLog;

    public HandoffDeliveryService(IRoleNotifier notifier, HandoffDeliveryLog log)
    {
        myNotifier = notifier;
        myLog = log;
    }

    public async Task ProcessOnceAsync(IReadOnlyList<RoleRow> roles, CancellationToken cancellationToken = default)
    {
        var roleMap = roles.ToDictionary(r => r.Role);
        foreach (var (roleName, roleInfo) in roleMap)
        {
            var outboxDir = Path.Combine(roleInfo.WorktreePath, ".blaxquad", "handoffs", "outbox");
            foreach (var path in HandoffQueue.HandoffFiles(outboxDir))
            {
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
                    myLog.Append(["error", path, exception.Message]);
                    try
                    {
                        Fail(path, exception.Message);
                    }
                    catch (Exception nested)
                    {
                        myLog.Append(["failed-to-archive", path, nested.Message]);
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
            {
                continue;
            }
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
                myLog.Append(["notify-failed", role.Role, exception.Message]);
            }
        }
    }

    private async Task DeliverAsync(Dictionary<string, RoleRow> roles, string senderRole, string path, CancellationToken cancellationToken)
    {
        var document = HandoffJson.Read(path);

        var deliveries = new List<(string Recipient, RoleRow RoleInfo)>();
        foreach (var recipient in document.To)
        {
            if (!roles.TryGetValue(recipient, out var roleInfo))
            {
                throw new InvalidOperationException($"unknown recipient {recipient}");
            }
            deliveries.Add((recipient, roleInfo));
        }

        var filename = Path.GetFileName(path);
        foreach (var (recipient, roleInfo) in deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(roleInfo.WorktreePath, ".blaxquad", "handoffs", "inbox", "new", filename);
            var delivered = document with { Recipient = recipient, EnqueuedAt = Timestamps.Now() };
            WriteRecipientArtifact(target, delivered);
        }

        var sentDir = Path.Combine(roles[senderRole].WorktreePath, ".blaxquad", "handoffs", "sent");
        MoveWithCollision(path, sentDir);
        myLog.Append(["delivered", path]);

        foreach (var (_, roleInfo) in deliveries)
        {
            try
            {
                await myNotifier.NotifyAsync(roleInfo.Role, cancellationToken);
            }
            catch (Exception exception)
            {
                myLog.Append(["notify-failed", roleInfo.Role, exception.Message]);
            }
        }
    }

    private static bool HasPendingInbox(string worktreePath)
    {
        var handoffs = Path.Combine(worktreePath, ".blaxquad", "handoffs", "inbox");
        return new[] { "new", "in_process" }.Any(state =>
        {
            var directory = Path.Combine(handoffs, state);
            return Directory.Exists(directory)
                && Directory.EnumerateFiles(directory, "*" + HandoffDocument.FileSuffix, SearchOption.TopDirectoryOnly).Any();
        });
    }

    /// <summary>Writes a recipient's durable copy only if it does not already exist, so a retried delivery never
    /// overwrites an already-persisted artifact.</summary>
    private static void WriteRecipientArtifact(string target, HandoffDocument delivered)
    {
        if (Path.Exists(target))
        {
            return;
        }
        HandoffJson.Write(target, delivered);
    }

    private static void MoveWithCollision(string source, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileName(source);
        var target = Path.Combine(targetDir, baseName);
        if (Path.Exists(target))
        {
            target = Path.Combine(targetDir, $"{Timestamps.Now()}_{baseName}");
        }
        File.Move(source, target);
    }

    private void Fail(string path, string reason)
    {
        var handoffsDir = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
        var failedDir = Path.Combine(handoffsDir, "failed");
        myLog.Append(["failed", path, reason]);
        File.WriteAllText(path + ".error", reason + "\n");
        MoveWithCollision(path, failedDir);
    }
}
