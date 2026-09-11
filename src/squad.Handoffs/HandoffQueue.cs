using squad.Process;

namespace squad.Handoffs;

/// <summary>Enumerates queued handoffs in stable order and renders their command-line representation.</summary>
public static class HandoffQueue
{
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
        output.WriteLine($"PRIORITY: {Priority.Format(document.Priority)}");
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
        output.WriteLine($"PRIORITY: {Priority.Format(firstPriority)}");
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
