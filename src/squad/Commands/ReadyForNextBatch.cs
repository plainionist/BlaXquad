using global::squad.Agent;
using global::squad.Agent.Cli;

namespace squad.Commands;

static class ReadyForNextBatch
{
    public static int Run(string[] args)
    {
        var inbox = Path.Combine(ProjectRoot.ResolveViaGit(), ".blaxquad", "handoffs", "inbox");
        var newDir = Path.Combine(inbox, "new");
        var inProcessDir = Path.Combine(inbox, "in_process");
        var completedDir = Path.Combine(inbox, "completed");

        Directory.CreateDirectory(newDir);
        Directory.CreateDirectory(inProcessDir);
        Directory.CreateDirectory(completedDir);

        try
        {
            var inProcessBatches = HandoffQueue.BatchDirs(inProcessDir);
            var inProcessFiles = HandoffQueue.HandoffFiles(inProcessDir);

            if (inProcessFiles.Count > 0)
                Fail(2, "TASK_IN_PROCESS_IS_SINGLE: use ready_for_next or done_with_current.", inProcessFiles);

            if (inProcessBatches.Count > 1)
                Fail(2, "AMBIGUOUS_TASK_STATE: multiple batches are already in process.", inProcessBatches);

            if (inProcessBatches.Count == 1)
            {
                HandoffQueue.PrintBatch(Console.Out, inProcessBatches[0]);
                return 0;
            }

            var newFiles = HandoffQueue.HandoffFiles(newDir);
            if (newFiles.Count == 0)
            {
                Console.Out.WriteLine("NO_TASK");
                return 0;
            }

            var batchPriority = HandoffHeaders.HeaderField(newFiles[0], "priority") ?? "50";
            var batchDir = NewBatchDir(inProcessDir);
            var selectedFiles = newFiles.Where(f => (HandoffHeaders.HeaderField(f, "priority") ?? "50") == batchPriority).ToList();

            Directory.CreateDirectory(batchDir);
            foreach (var sourceFile in selectedFiles)
            {
                var targetFile = Path.Combine(batchDir, Path.GetFileName(sourceFile));
                if (Path.Exists(targetFile))
                    Fail(2, $"AMBIGUOUS_TASK_STATE: target batch file already exists: {targetFile}");

                File.Move(sourceFile, targetFile);
                HandoffHeaders.SetHeader(targetFile, "dequeued_at", Timestamps.Now());
            }

            if (selectedFiles.Count == 0)
                Fail(2, $"AMBIGUOUS_TASK_STATE: no tasks selected for batch priority {batchPriority}.");

            HandoffQueue.PrintBatch(Console.Out, batchDir);
            return 0;
        }
        catch (CliExitException ex)
        {
            if (!string.IsNullOrEmpty(ex.Message))
                Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
    }

    static string NewBatchDir(string inProcessDir)
    {
        var suffix = 1;
        while (true)
        {
            var dir = Path.Combine(inProcessDir, $"batch_{Timestamps.IdNow()}_{suffix:D6}");
            if (!Path.Exists(dir))
                return dir;
            suffix++;
        }
    }

    static void Fail(int status, string headline, IReadOnlyList<string>? items = null)
    {
        var lines = new List<string> { headline };
        if (items is not null)
            lines.AddRange(items.Select(i => $"- {i}"));
        throw new CliExitException(status, string.Join("\n", lines));
    }
}



