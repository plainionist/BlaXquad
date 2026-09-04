using global::squad.Agent;
using global::squad.Agent.Cli;
using global::squad.Agent.Configuration;

namespace squad.Commands;

static class ReadyForNextTask
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

            if (inProcessBatches.Count > 0)
                Fail(2, "TASK_IN_PROCESS_IS_BATCH: use ready_for_next or done_with_current.", inProcessBatches);

            if (inProcessFiles.Count > 1)
                Fail(2, "AMBIGUOUS_TASK_STATE: multiple tasks are already in process.", inProcessFiles);

            if (inProcessFiles.Count == 1)
            {
                HandoffQueue.PrintTask(Console.Out, inProcessFiles[0]);
                return 0;
            }

            var newFiles = HandoffQueue.HandoffFiles(newDir);
            if (newFiles.Count == 0)
            {
                Console.Out.WriteLine("NO_TASK");
                return 0;
            }

            var sourceFile = newFiles[0];
            var targetFile = Path.Combine(inProcessDir, Path.GetFileName(sourceFile));
            if (Path.Exists(targetFile))
                Fail(2, $"AMBIGUOUS_TASK_STATE: target in-process file already exists: {targetFile}");

            File.Move(sourceFile, targetFile);
            HandoffHeaders.SetHeader(targetFile, "dequeued_at", Timestamps.Now());
            HandoffQueue.PrintTask(Console.Out, targetFile);
            return 0;
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



