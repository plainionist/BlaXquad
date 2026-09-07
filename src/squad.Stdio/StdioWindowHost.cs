using squad.Hosting.Abstractions;
using squad.Ui.Abstractions;
using squad.Ui.Protocol;

namespace squad.Stdio;

/// <summary>
/// Owns a headless <see cref="UiProtocolSession"/> transported over standard input and standard output. Every
/// non-EOF input line is passed unchanged to the session; every outgoing protocol envelope is written as exactly
/// one flushed stdout line. Startup does not complete until the UI sends "ui.ready", mirroring the native window
/// host so headless and visual transports share identical protocol behavior.
/// </summary>
public sealed class StdioWindowHost : IWindowHost
{
    private readonly UiProtocolSession mySession;
    private readonly TaskCompletionSource myClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myUiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource myPumpCancellation = new();
    private readonly object myWriteLock = new();
    private readonly object myStopLock = new();
    private Task? myInputPump;
    private Task? myStop;
    private bool myStarted;

    public StdioWindowHost(ISquadUi ui)
    {
        mySession = new(
            ui,
            SendSerializedMessage,
            () => myUiReady.TrySetResult(),
            () => _ = StopAsync());
    }

    public bool HasCloseSignal => myClosed.Task.IsCompleted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (myStarted)
        {
            return Task.CompletedTask;
        }
        myStarted = true;

        mySession.AttachUiEventSources();
        myInputPump = Task.Run(RunInputPumpAsync);
        return WaitForUiReadyAsync(cancellationToken);
    }

    public Task SessionsStartedAsync(CancellationToken cancellationToken = default) =>
        mySession.SessionsStartedAsync(cancellationToken);

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) =>
        myClosed.Task.WaitAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (myStopLock)
            return myStop ??= StopCoreAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RunInputPumpAsync()
    {
        try
        {
            while (!myPumpCancellation.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await Console.In.ReadLineAsync(myPumpCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // The underlying stream faulted (e.g. torn down by a racing stop); this background task is
                    // never awaited, so a read failure must end the pump the same way EOF does rather than fault
                    // this task and go unobserved.
                    break;
                }
                if (line is null)
                {
                    // End of standard input: treat exactly like the native window being closed.
                    break;
                }
                if (myPumpCancellation.IsCancellationRequested)
                {
                    // A stop was requested while this read was already in flight. Console.In's cancellation token
                    // does not interrupt a read already in progress, so a line can still arrive after cancellation;
                    // the protocol session may already be disposed, so do not forward it.
                    break;
                }
                await mySession.ReceiveMessageAsync(line);
            }
        }
        finally
        {
            myClosed.TrySetResult();
        }
    }

    private async Task WaitForUiReadyAsync(CancellationToken cancellationToken)
    {
        var cancellation = cancellationToken.CanBeCanceled
            ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            : new TaskCompletionSource().Task;
        await Task.WhenAny(myUiReady.Task, myClosed.Task, cancellation);
        if (myUiReady.Task.IsCompleted)
        {
            await myUiReady.Task;
            return;
        }
        // Neither cancellation nor EOF may leave startup hanging: surface cancellation, otherwise let a close
        // signal that arrived before "ui.ready" complete startup so shutdown can proceed without another line.
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task StopCoreAsync()
    {
        myPumpCancellation.Cancel();
        mySession.DetachUiEventSources();
        try
        {
            await mySession.DisposeAsync();
        }
        finally
        {
            myClosed.TrySetResult();
            myUiReady.TrySetCanceled();
        }
    }

    private void SendSerializedMessage(string message)
    {
        lock (myWriteLock)
        {
            Console.Out.WriteLine(message);
            Console.Out.Flush();
        }
    }
}
