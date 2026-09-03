namespace squad.Core;

public sealed class CleanupLease : IDisposable
{
    private readonly string myStateDir;
    private readonly FileStream myLockFile;
    private bool myDisposed;

    internal CleanupLease(string stateDir, FileStream lockFile)
    {
        myStateDir = stateDir;
        myLockFile = lockFile;
    }

    public void RemoveStaleMetadata()
    {
        var metadata = Path.Combine(myStateDir, "host.json");
        if (File.Exists(metadata))
            File.Delete(metadata);
    }

    public void Dispose()
    {
        if (myDisposed)
            return;
        myDisposed = true;
        try { HostLease.UnlockFile(myLockFile); } catch (Exception) { }
        myLockFile.Dispose();
    }
}



