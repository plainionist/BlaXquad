using squad.Process;
using squad.Configuration;
using squad.Handoffs;

namespace squad.Commands;

static class DoneWithCurrentBatch
{
    public static int Run(string[] args)
    {
        var root = HandoffQueue.Root(ProjectRoot.ResolveViaGit());
        var inProcessDir = HandoffQueue.InProcessInbox(root);
        var completedDir = HandoffQueue.CompletedInbox(root);

        Directory.CreateDirectory(inProcessDir);
        Directory.CreateDirectory(completedDir);

        try
        {
            var inProcessBatches = HandoffQueue.BatchDirs(inProcessDir);
            var inProcessFiles = HandoffQueue.HandoffFiles(inProcessDir);

            if (inProcessFiles.Count > 0)
            {
                Fail(2, "CURRENT_WORK_IS_SINGLE_TASK: use done_with_current.", inProcessFiles);
            }

            if (inProcessBatches.Count == 0)
            {
                Fail(1, "NO_CURRENT_BATCH");
            }

            if (inProcessBatches.Count > 1)
            {
                Fail(2, "AMBIGUOUS_TASK_STATE: multiple batches are in process.", inProcessBatches);
            }

            var sourceDir = inProcessBatches[0];
            var batchFiles = HandoffQueue.HandoffFiles(sourceDir);
            var targetDir = Path.Combine(completedDir, Path.GetFileName(sourceDir));
            var completedAt = Timestamps.NowOffset();

            if (batchFiles.Count == 0)
            {
                Fail(2, $"AMBIGUOUS_TASK_STATE: batch contains no tasks: {sourceDir}");
            }
            if (Path.Exists(targetDir))
            {
                Fail(2, $"AMBIGUOUS_TASK_STATE: completed batch already exists: {targetDir}");
            }

            Directory.CreateDirectory(targetDir);
            foreach (var sourceFile in batchFiles)
            {
                HandoffJson.Update(sourceFile, document => document with { CompletedAt = completedAt });
                var targetFile = Path.Combine(targetDir, Path.GetFileName(sourceFile));
                if (Path.Exists(targetFile))
                {
                    Fail(2, $"AMBIGUOUS_TASK_STATE: completed batch file already exists: {targetFile}");
                }

                File.Move(sourceFile, targetFile);
                Console.Out.WriteLine($"COMPLETED: {targetFile}");
            }

            Directory.Delete(sourceDir);
            Console.Out.WriteLine($"COMPLETED_BATCH: {targetDir}");
            return ReadyForNextBatch.Run(args);
        }
        catch (CliExitException ex)
        {
            if (!string.IsNullOrEmpty(ex.Message))
            {
                Console.Error.WriteLine(ex.Message);
            }
            return ex.ExitCode;
        }
    }

    static void Fail(int status, string headline, IReadOnlyList<string>? items = null)
    {
        var lines = new List<string> { headline };
        if (items is not null)
        {
            lines.AddRange(items.Select(i => $"- {i}"));
        }
        throw new CliExitException(status, string.Join("\n", lines));
    }
}



