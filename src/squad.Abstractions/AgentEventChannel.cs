using System.Threading.Channels;
using squad.Abstractions.Agents;

namespace squad.Abstractions;

public sealed class AgentEventChannel : IAsyncDisposable
{
    private const int myDefaultCapacity = 100;
    private static readonly TimeSpan myDefaultWriteTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<AgentEvent> myChannel;
    private readonly TimeSpan myWriteTimeout;
    private readonly Action<Exception>? myOnOverload;
    private readonly CancellationTokenSource myDisposalCts = new();
    private readonly SemaphoreSlim myWriteGate = new(1, 1);
    private int myOverflowFaulted;
    private bool myDisposed;

    public AgentEventChannel(
        int capacity = myDefaultCapacity,
        TimeSpan? writeTimeout = null,
        Action<Exception>? onOverload = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");

        myWriteTimeout = writeTimeout ?? myDefaultWriteTimeout;
        myOnOverload = onOverload;
        myChannel = Channel.CreateBounded<AgentEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int Depth => myChannel.Reader.Count;

    public void Publish(AgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        if (myDisposed)
            return;

        if (myChannel.Writer.TryWrite(agentEvent))
            return;

        if (!myWriteGate.Wait(0))
        {
            FailTerminal(new InvalidOperationException(
                $"Agent event channel sustained overload: bounded overflow admission is full at capacity {myChannel.Reader.Count}."));
            return;
        }

        _ = PublishAsyncCore(agentEvent, CancellationToken.None, gateAlreadyAcquired: true);
    }

    public Task PublishAsync(AgentEvent agentEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        if (myDisposed)
            return Task.CompletedTask;

        if (myChannel.Writer.TryWrite(agentEvent))
            return Task.CompletedTask;

        if (!myWriteGate.Wait(0))
        {
            var overloadException = new InvalidOperationException(
                $"Agent event channel sustained overload: bounded overflow admission is full at capacity {myChannel.Reader.Count}.");
            FailTerminal(overloadException);
            return Task.FromException(overloadException);
        }

        return PublishAsyncCore(agentEvent, cancellationToken, gateAlreadyAcquired: true);
    }

    private async Task PublishAsyncCore(AgentEvent agentEvent, CancellationToken cancellationToken, bool gateAlreadyAcquired)
    {
        if (!gateAlreadyAcquired)
        {
            await myWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (myChannel.Writer.TryWrite(agentEvent))
                return;

            await PublishWriteWithTimeoutAsync(agentEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (myDisposalCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ChannelClosedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            FailTerminal(exception);
            throw;
        }
        finally
        {
            myWriteGate.Release();
        }
    }

    private async Task PublishWriteWithTimeoutAsync(AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myDisposalCts.Token);
        var writeTask = myChannel.Writer.WriteAsync(agentEvent, linkedCts.Token).AsTask();
        var timeoutTask = Task.Delay(myWriteTimeout, timeoutCts.Token);
        try
        {
            var completedTask = await Task.WhenAny(writeTask, timeoutTask).ConfigureAwait(false);
            if (completedTask == timeoutTask)
            {
                linkedCts.Cancel();
                var overloadException = new InvalidOperationException(
                    $"Agent event channel sustained overload: capacity of {myChannel.Reader.Count} was exceeded for over {myWriteTimeout.TotalSeconds:0.##}s.");
                FailTerminal(overloadException);
                throw overloadException;
            }

            await writeTask.ConfigureAwait(false);
        }
        finally
        {
            timeoutCts.Cancel();
        }
    }

    public IAsyncEnumerable<AgentEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return myChannel.Reader.ReadAllAsync(cancellationToken);
    }

    public void Complete(Exception? error = null)
    {
        myChannel.Writer.TryComplete(error);
    }

    public ValueTask DisposeAsync()
    {
        if (myDisposed)
            return ValueTask.CompletedTask;

        myDisposed = true;
        myDisposalCts.Cancel();
        myChannel.Writer.TryComplete();
        myDisposalCts.Dispose();
        return ValueTask.CompletedTask;
    }

    private void FailTerminal(Exception exception)
    {
        if (myDisposed || myChannel.Reader.Completion.IsCompleted)
            return;

        if (Interlocked.Exchange(ref myOverflowFaulted, 1) != 0)
            return;

        myChannel.Writer.TryComplete(exception);
        myOnOverload?.Invoke(exception);
    }
}



