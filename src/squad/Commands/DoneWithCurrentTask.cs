using global::squad.Agent;

namespace squad.Commands;

static class DoneWithCurrentTask
{
    public static int Run(string[] args)
    {
        var inbox = Path.Combine(ProjectRoot.ResolveViaGit(), ".blaxquad", "handoffs", "inbox");
        var inProcessDir = Path.Combine(inbox, "in_process");
        var completedDir = Path.Combine(inbox, "completed");

        Directory.CreateDirectory(inProcessDir);
        Directory.CreateDirectory(completedDir);

        try
        {
            var inProcessBatches = HandoffQueue.BatchDirs(inProcessDir);
            var inProcessFiles = HandoffQueue.HandoffFiles(inProcessDir);

            if (inProcessBatches.Count > 0)
                Fail(2, "CURRENT_WORK_IS_BATCH: use done_with_current.", inProcessBatches);

            if (inProcessFiles.Count == 0)
                Fail(1, "NO_CURRENT_TASK");

            if (inProcessFiles.Count > 1)
                Fail(2, "AMBIGUOUS_TASK_STATE: multiple tasks are in process.", inProcessFiles);

            var sourceFile = inProcessFiles[0];
            var targetFile = Path.Combine(completedDir, Path.GetFileName(sourceFile));

            HandoffHeaders.SetHeader(sourceFile, "completed_at", Timestamps.Now());
            if (Path.Exists(targetFile))
                Fail(2, $"AMBIGUOUS_TASK_STATE: completed file already exists: {targetFile}");

            File.Move(sourceFile, targetFile);
            Console.Out.WriteLine($"COMPLETED: {targetFile}");
            return ReadyForNextTask.Run(args);
        }
        catch (CliExitException ex)
        {
            if (!string.IsNullOrEmpty(ex.Message))
                Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
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



