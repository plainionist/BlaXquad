namespace squad.Handoff;

/// <summary>Per-worktree monotonically increasing counter used to break ties in handoff filenames.</summary>
public static class SequenceCounter
{
    public static string Next(string handoffsStateDir)
    {
        Directory.CreateDirectory(handoffsStateDir);
        var seqFile = Path.Combine(handoffsStateDir, "sequence");
        var lockFile = Path.Combine(handoffsStateDir, "sequence.lock");

        FileStream? handle = null;
        while (handle is null)
        {
            try
            {
                // Atomic create-exclusive serializes sequence updates across processes.
                handle = new FileStream(lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }

        try
        {
            long last = 0;
            if (File.Exists(seqFile))
                long.TryParse(File.ReadAllText(seqFile).Trim(), out last);
            var formatted = (last + 1).ToString("D6");
            File.WriteAllText(seqFile, formatted + "\n");
            return formatted;
        }
        finally
        {
            handle.Dispose();
            File.Delete(lockFile);
        }
    }
}



