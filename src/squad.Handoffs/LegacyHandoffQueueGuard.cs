namespace squad.Handoffs;

/// <summary>
/// Rejects a handoff queue operation while a role's handoff directory still contains legacy, pre-JSON ".handoff"
/// artifacts, so a normal queue operation can never process an ambiguous mixture of legacy and JSON artifacts.
/// A legacy queue must be drained with the previous release, or discarded by a fresh (non-continued) launch, which
/// clears every queue state before this guard ever runs; there is no dual-format reader or migrator.
/// </summary>
public static class LegacyHandoffQueueGuard
{
    private static readonly string[] QueueStates =
    [
        "outbox",
        "sent",
        "failed",
        Path.Combine("inbox", "new"),
        Path.Combine("inbox", "in_process"),
        Path.Combine("inbox", "completed"),
    ];

    /// <summary>Throws <see cref="LegacyHandoffQueueException"/> if any queue state under <paramref name="handoffsDir"/>
    /// (a role's ".blaxquad/handoffs" directory) still contains a legacy ".handoff" file.</summary>
    public static void EnsureNoLegacyArtifacts(string handoffsDir)
    {
        var legacyFiles = QueueStates
            .Select(state => Path.Combine(handoffsDir, state))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.handoff", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (legacyFiles.Count == 0)
        {
            return;
        }

        throw new LegacyHandoffQueueException(
            $"LEGACY_HANDOFF_QUEUE: {handoffsDir} still contains {legacyFiles.Count} legacy '.handoff' artifact(s) " +
            "from a pre-JSON release. Drain this queue with the previous BlaXquad release, or discard it with a " +
            "fresh (non-continued) launch, before using this release.\n" +
            string.Join("\n", legacyFiles.Select(path => $"- {path}")));
    }
}
