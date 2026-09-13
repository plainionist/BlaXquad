using Microsoft.Extensions.Logging;
using squad.Domain;

namespace squad.Handoffs.Delivery;

/// <summary>
/// Moves outbound handoff files atomically into recipient inboxes, archives sender artifacts, and wakes recipients.
/// Per-file delivery failures are archived without stopping the scan.
/// </summary>
sealed class HandoffDeliveryService
{
    private readonly IRoleNotifier myNotifier;
    private readonly ILogger myLogger;

    public HandoffDeliveryService(IRoleNotifier notifier, ILogger logger)
    {
        myNotifier = notifier;
        myLogger = logger;
    }

    public async Task ProcessOnceAsync(IReadOnlyList<SquadMemberDefinition> members, CancellationToken cancellationToken = default)
    {
        var memberMap = members.ToDictionary(member => member.Id);

        foreach (var (memberId, memberInfo) in memberMap)
        {
            var outboxDir = HandoffQueue.Outbox(HandoffQueue.Root(memberInfo.WorktreePath));

            foreach (var path in HandoffQueue.HandoffFiles(outboxDir))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await DeliverAsync(memberMap, memberId, path, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    myLogger.LogError(exception, "Handoff delivery failed for '{HandoffPath}'.", path);

                    try
                    {
                        Fail(path, exception.Message);
                    }
                    catch (Exception nested)
                    {
                        myLogger.LogError(nested, "Failed to archive handoff '{HandoffPath}' after a delivery failure.", path);
                    }
                }
            }

        }
    }

    private async Task DeliverAsync(Dictionary<SquadMemberId, SquadMemberDefinition> members, SquadMemberId senderMember, string path, CancellationToken cancellationToken)
    {
        var document = HandoffJson.Read(path);

        var deliveries = new List<(SquadMemberId Recipient, SquadMemberDefinition MemberInfo)>();

        foreach (var recipient in document.To)
        {

            if (!members.TryGetValue(recipient, out var memberInfo))
            {
                throw new InvalidOperationException($"unknown recipient {recipient}");
            }

            deliveries.Add((recipient, memberInfo));
        }

        var filename = Path.GetFileName(path);

        foreach (var (recipient, memberInfo) in deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(HandoffQueue.NewInbox(HandoffQueue.Root(memberInfo.WorktreePath)), filename);
            var delivered = document with { Recipient = recipient, EnqueuedAt = Timestamps.NowOffset() };
            WriteRecipientArtifact(target, delivered);
        }

        var sentDir = HandoffQueue.Sent(HandoffQueue.Root(members[senderMember].WorktreePath));
        MoveWithCollision(path, sentDir);

        foreach (var (_, memberInfo) in deliveries)
        {
            try
            {
                await myNotifier.NotifyAsync(memberInfo.Id, cancellationToken);
            }
            catch (Exception exception)
            {
                myLogger.LogWarning(exception, "Failed to notify '{MemberId}' of handoff '{HandoffPath}'.", memberInfo.Id.Value, path);
            }
        }
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
        var failedDir = HandoffQueue.Failed(handoffsDir);
        File.WriteAllText(path + ".error", reason + "\n");
        MoveWithCollision(path, failedDir);
    }
}
