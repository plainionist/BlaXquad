using squad.Hosting.Abstractions;
using squad.Ui.Abstractions;
using squad.Ui.Protocol;

namespace squad.Hosting.Stdio;

/// <summary>
/// Owns a headless <see cref="UiProtocolSession"/> transported over standard input and standard output. Every
/// non-EOF input line is passed unchanged to the session; every outgoing protocol envelope is written as exactly
/// one flushed stdout line. Startup does not complete until the UI sends "ui.ready", mirroring the native window
/// host so headless and visual transports share identical protocol behavior. Internal: this plug-in's only public
/// surface is <see cref="StdioHostingFactory"/>, loaded at process startup through
/// <see cref="squad.Hosting.Abstractions.IHostingFactory"/>.
/// </summary>
/// <remarks>
/// The input pump owns line framing and admission only: it dispatches each complete line to
/// <see cref="UiProtocolSession.ReceiveMessageAsync"/> and immediately reads the next line rather than waiting for
/// that command's domain operation to complete. This mirrors the visual UI, where one role's slow prompt cannot
/// prevent another role's command from being accepted. The application view model remains the sole authority for
/// per-role prompt and abort serialization. Every dispatched command is tracked so shutdown can drain them - and
/// observe any fault - before disposing the session, avoiding both unobserved-task faults and command/disposal
/// races.
/// </remarks>
sealed class StdioWindowHost : IWindowHost
{
    private readonly UiProtocolSession mySession;
    private readonly TaskCompletionSource myClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myUiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource myPumpCancellation = new();
    private readonly object myWriteLock = new();
    private readonly object myStopLock = new();
    private readonly object myDispatchedCommandsLock = new();
    private readonly HashSet<Task> myDispatchedCommands = [];
    private Task? myInputPump;
    private Task? myStop;
    private bool myStarted;

    public StdioWindowHost(ISquadUi ui, IIssueCatalog issueCatalog, IWorkspaceTools workspaceTools)
    {
        mySession = new(
            ui,
            issueCatalog,
            workspaceTools,
            SendSerializedMessage,
            () => myUiReady.TrySetResult());
    }

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
                // A stop may already be in progress: Console.In's cancellation token does not interrupt a read
                // already in progress, so a line can still arrive after Cancel() was called. DispatchCommand
                // re-checks cancellation under the same lock StopCoreAsync uses to snapshot dispatched commands,
                // so such a late line is never admitted once a stop has started.
                DispatchCommand(line);
            }
        }
        finally
        {
            myClosed.TrySetResult();
        }
    }

    /// <summary>
    /// Starts processing one received line without waiting for its domain operation to complete, so a role's slow
    /// prompt or abort never delays admitting the next line - for that role or any other. The dispatched task is
    /// tracked until it finishes so shutdown can drain every in-flight command before disposing the session. Does
    /// nothing once a stop has started, so no command can be admitted after <see cref="StopCoreAsync"/> has taken
    /// its snapshot of what to drain.
    /// </summary>
    private void DispatchCommand(string line)
    {
        lock (myDispatchedCommandsLock)
        {
            if (myPumpCancellation.IsCancellationRequested)
            {
                return;
            }
            var task = mySession.ReceiveMessageAsync(line);
            myDispatchedCommands.Add(task);
            _ = ObserveDispatchedCommandAsync(task);
        }
    }

    private async Task ObserveDispatchedCommandAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // UiProtocolSession.ReceiveMessageAsync already converts every failure into a "protocol.error"
            // envelope and never faults; this catch exists only so a dispatched command could never surface as an
            // unobserved task exception once the pump has moved on to reading further lines.
        }
        finally
        {
            lock (myDispatchedCommandsLock)
            {
                myDispatchedCommands.Remove(task);
            }
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
        Task[] pending;
        lock (myDispatchedCommandsLock)
        {
            // Cancelling and snapshotting under the same lock DispatchCommand uses to check-and-admit closes the
            // race: either a line was already admitted and its task is in this snapshot, or cancellation is now
            // visible to DispatchCommand before it can add anything else. A line whose read cannot be interrupted
            // mid-flight (see RunInputPumpAsync) may still arrive afterwards, but DispatchCommand will refuse it.
            myPumpCancellation.Cancel();
            pending = [.. myDispatchedCommands];
        }
        if (pending.Length > 0)
        {
            await Task.WhenAll(pending);
        }
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
