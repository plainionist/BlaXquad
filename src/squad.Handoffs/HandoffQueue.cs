using squad.Process;

namespace squad.Handoffs;

/// <summary>Enumerates queued handoffs in stable order and renders their command-line representation.</summary>
public static class HandoffQueue
{
    /// <summary>Returns handoff files in stable name order, or an empty list when the directory does not exist.</summary>
    public static IReadOnlyList<string> HandoffFiles(string dir)
    {
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".handoff", StringComparison.Ordinal))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Returns batch directories in stable name order, or an empty list when the directory does not exist.</summary>
    public static IReadOnlyList<string> BatchDirs(string dir)
    {
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        return Directory.EnumerateDirectories(dir)
            .Where(d => Path.GetFileName(d).StartsWith("batch_", StringComparison.Ordinal))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Renders one task with normalized metadata and its original payload.</summary>
    public static void PrintTask(TextWriter output, string filePath)
    {
        var taskName = HandoffHeaders.HeaderField(filePath, "task");
        output.WriteLine($"TASK: {filePath}");
        output.WriteLine($"FROM: {HandoffHeaders.HeaderField(filePath, "from") ?? "unknown"}");
        output.WriteLine($"TYPE: {HandoffHeaders.HeaderField(filePath, "type") ?? "unknown"}");
        output.WriteLine($"PRIORITY: {HandoffHeaders.HeaderField(filePath, "priority") ?? "50"}");
        if (taskName is not null)
            output.WriteLine($"TASK_NAME: {taskName}");
        output.WriteLine("PAYLOAD:");
        output.Write(HandoffHeaders.Body(filePath));
    }

    /// <summary>Renders every task in a batch and rejects an empty batch as ambiguous state.</summary>
    public static void PrintBatch(TextWriter output, string batchDir)
    {
        var files = HandoffFiles(batchDir);
        if (files.Count == 0)
            throw new CliExitException(2, $"AMBIGUOUS_TASK_STATE: batch contains no tasks: {batchDir}");

        output.WriteLine($"BATCH: {batchDir}");
        output.WriteLine($"COUNT: {files.Count}");
        output.WriteLine($"PRIORITY: {HandoffHeaders.HeaderField(files[0], "priority") ?? "50"}");
        for (var i = 0; i < files.Count; i++)
        {
            output.WriteLine();
            output.WriteLine($"BATCH_ITEM: {i + 1}");
            PrintTask(output, files[i]);
        }
    }
}


