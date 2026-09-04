namespace squad.Core.RoleOperations;

/// <summary>
/// Holds a role's prompt serialization slot until disposed. Prompts for the same role wait for one another in
/// acquisition order.
/// </summary>
internal sealed class RolePromptLease : IDisposable
{
    private readonly SemaphoreSlim myPromptLock;
    private bool myDisposed;

    internal RolePromptLease(SemaphoreSlim promptLock)
    {
        myPromptLock = promptLock;
    }

    public void Dispose()
    {
        if (myDisposed)
            return;
        myDisposed = true;
        myPromptLock.Release();
    }
}
