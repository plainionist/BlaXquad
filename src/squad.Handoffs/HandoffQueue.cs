using squad.Process;

namespace squad.Handoffs;

/// <summary>Enumerates queued handoffs in stable order, renders their command-line representation, and names the
/// canonical durable directory layout under one worktree's ".blaxquad/handoffs" root - the single owner of that
/// layout, so no caller assembles its literal segments itself.</summary>
public static class HandoffQueue
{
    /// <summary>The ".blaxquad/handoffs" root for one worktree's durable handoff state.</summary>
    public static string Root(string worktreePath) => Path.Combine(worktreePath, ".blaxquad", "handoffs");

    /// <summary>Where a sender queues a handoff not yet delivered to any recipient.</summary>
    public static string Outbox(string root) => Path.Combine(root, "outbox");

    /// <summary>Where a sender's durably delivered handoffs are archived.</summary>
    public static string Sent(string root) => Path.Combine(root, "sent");

    /// <summary>Where a sender's or recipient's failed handoffs are archived.</summary>
    public static string Failed(string root) => Path.Combine(root, "failed");

    /// <summary>Where a recipient's not-yet-claimed handoffs are delivered.</summary>
    public static string NewInbox(string root) => Path.Combine(root, "inbox", "new");

    /// <summary>Where a recipient's currently claimed task or batch is held.</summary>
    public static string InProcessInbox(string root) => Path.Combine(root, "inbox", "in_process");

    /// <summary>Where a recipient's completed handoffs are archived.</summary>
    public static string CompletedInbox(string root) => Path.Combine(root, "inbox", "completed");

    /// <summary>Returns handoff files in stable name order, or an empty list when the directory does not exist.</summary>
    public static IReadOnlyList<string> HandoffFiles(string dir)
    {

        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(HandoffDocument.FileSuffix, StringComparison.Ordinal))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Returns batch directories in stable name order, or an empty list when the directory does not exist.</summary>
    public static IReadOnlyList<string> BatchDirs(string dir)
    {

        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(dir)
            .Where(d => Path.GetFileName(d).StartsWith("batch_", StringComparison.Ordinal))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Renders one task with normalized metadata and its derived payload.</summary>
    public static void PrintTask(TextWriter output, string filePath)
    {
        var document = HandoffJson.Read(filePath);
        output.WriteLine($"TASK: {filePath}");
        output.WriteLine($"FROM: {document.From}");
        output.WriteLine($"TYPE: {TypeLabel(document.Kind)}");
        output.WriteLine($"PRIORITY: {document.Priority}");

        if (document.Kind == HandoffKind.GitHandoff)
        {
            output.WriteLine($"TASK_NAME: {document.GitHandoff!.Task}");
        }

        output.WriteLine("PAYLOAD:");
        output.WriteLine(document.RenderPayload());
    }

    /// <summary>Renders every task in a batch and rejects an empty batch as ambiguous state.</summary>
    public static void PrintBatch(TextWriter output, string batchDir)
    {
        var files = HandoffFiles(batchDir);

        if (files.Count == 0)
        {
            throw new CliExitException(2, $"AMBIGUOUS_TASK_STATE: batch contains no tasks: {batchDir}");
        }

        var firstPriority = HandoffJson.Read(files[0]).Priority;
        output.WriteLine($"BATCH: {batchDir}");
        output.WriteLine($"COUNT: {files.Count}");
        output.WriteLine($"PRIORITY: {firstPriority}");

        for (var i = 0; i < files.Count; i++)
        {
            output.WriteLine();
            output.WriteLine($"BATCH_ITEM: {i + 1}");
            PrintTask(output, files[i]);
        }
    }

    private static string TypeLabel(HandoffKind kind) => kind switch
    {
        HandoffKind.GitHandoff => "git_handoff",
        HandoffKind.Note => "note",
        _ => "unknown",
    };
}
