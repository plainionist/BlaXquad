namespace squad.Application;

/// <summary>
/// Holds a member's prompt serialization slot until disposed. Prompts for the same member wait for one another in
/// acquisition order.
/// </summary>
internal sealed class PromptLease : IDisposable
{
    private readonly SemaphoreSlim myPromptLock;
    private bool myDisposed;

    internal PromptLease(SemaphoreSlim promptLock)
    {
        myPromptLock = promptLock;
    }

    public void Dispose()
    {

        if (myDisposed)
        {
            return;
        }

        myDisposed = true;
        myPromptLock.Release();
    }
}
