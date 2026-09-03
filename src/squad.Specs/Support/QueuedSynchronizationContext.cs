using System.Collections.Concurrent;

namespace squad.Specs.Support;

public sealed class QueuedSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)>
        myCallbacks = new();

    public override void Post(SendOrPostCallback callback, object? state) =>
        myCallbacks.Enqueue((callback, state));

    public void Drain()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            while (myCallbacks.TryDequeue(out var work))
                work.Callback(work.State);
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}




