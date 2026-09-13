namespace squad.Runtime.Control;

/// <summary>Holds exclusive cleanup access to Headquarters metadata after proving that no live Headquarters instance owns the project.</summary>
internal sealed class HeadquartersCleanupLease : IDisposable
{
    private readonly string myStateDir;
    private readonly FileStream myLockFile;
    private bool myDisposed;

    internal HeadquartersCleanupLease(string stateDir, FileStream lockFile)
    {
        myStateDir = stateDir;
        myLockFile = lockFile;
    }

    public void RemoveStaleMetadata()
    {
        var metadata = Path.Combine(myStateDir, "host.json");

        if (File.Exists(metadata))
        {
            File.Delete(metadata);
        }
    }

    public void Dispose()
    {

        if (myDisposed)
        {
            return;
        }

        myDisposed = true;

        try { HeadquartersLease.UnlockFile(myLockFile); } catch (Exception) { }
        myLockFile.Dispose();
    }
}
