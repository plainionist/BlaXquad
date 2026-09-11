using System.Threading.Channels;
using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Application.Members;

/// <summary>
/// One member's independent, single-reader, bounded mailbox. It is the sole path through which a prompt, harness,
/// abort, interaction-response, provider-event, or session-terminal message reaches this member's
/// <see cref="MemberAggregate"/>, and the sole place that applies the outcome of a prompt, abort, or
/// interaction-response operation. A provider-event or session-terminal message never awaits provider I/O, so the
/// read loop applies it inline; a prompt, abort, or interaction-response message runs its provider I/O detached from
/// the read loop and reports its outcome back through <see cref="OperationOutcomeMessage"/>, so a slow or blocked
/// provider call for this member can never delay this member's own next message, and can never delay any other
/// member's processor - each of which owns an entirely independent mailbox and read loop.
/// </summary>
internal sealed class MemberProcessor
{
    // Bounded so a caller posting a message awaits mailbox capacity (backpressure) rather than an unbounded queue
    // growing without limit; multiple senders write concurrently (public commands and this processor's own
    // detached operations reporting their outcome), so SingleWriter is false.
    private readonly Channel<MemberMessage> myMailbox = Channel.CreateBounded<MemberMessage>(
        new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });

    private readonly object myInFlightLock = new();
    private readonly HashSet<Task> myInFlight = [];

    private readonly Func<Func<IAgentSession, CancellationToken, Task>, CancellationToken, Task> myExecutePrompt;
    private readonly Func<CancellationToken, Task> myExecuteAbort;
    private readonly Func<string, AgentPermissionResponse, CancellationToken, Task> myExecuteCompletePermission;
    private readonly Func<string, AgentInputResponse, CancellationToken, Task> myExecuteCompleteInput;
    private readonly Func<string, AgentElicitationResponse, CancellationToken, Task> myExecuteCompleteElicitation;
    private readonly Action<AgentEvent> myApplyEvent;
    private readonly Action<Exception> myMarkFailed;
    private readonly Task myLoop;

    internal MemberProcessor(
        Func<Func<IAgentSession, CancellationToken, Task>, CancellationToken, Task> executePrompt,
        Func<CancellationToken, Task> executeAbort,
        Func<string, AgentPermissionResponse, CancellationToken, Task> executeCompletePermission,
        Func<string, AgentInputResponse, CancellationToken, Task> executeCompleteInput,
        Func<string, AgentElicitationResponse, CancellationToken, Task> executeCompleteElicitation,
        Action<AgentEvent> applyEvent,
        Action<Exception> markFailed)
    {
        myExecutePrompt = executePrompt;
        myExecuteAbort = executeAbort;
        myExecuteCompletePermission = executeCompletePermission;
        myExecuteCompleteInput = executeCompleteInput;
        myExecuteCompleteElicitation = executeCompleteElicitation;
        myApplyEvent = applyEvent;
        myMarkFailed = markFailed;
        myLoop = RunLoopAsync();
    }

    internal Task SendPromptAsync(Func<IAgentSession, CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        PostAsync(completion => new SendPromptMessage(operation, cancellationToken, completion));

    internal Task AbortAsync(CancellationToken cancellationToken) =>
        PostAsync(completion => new AbortMessage(cancellationToken, completion));

    internal Task CompletePermissionAsync(string requestId, AgentPermissionResponse response, CancellationToken cancellationToken) =>
        PostAsync(completion => new CompletePermissionMessage(requestId, response, cancellationToken, completion));

    internal Task CompleteInputAsync(string requestId, AgentInputResponse response, CancellationToken cancellationToken) =>
        PostAsync(completion => new CompleteInputMessage(requestId, response, cancellationToken, completion));

    internal Task CompleteElicitationAsync(string requestId, AgentElicitationResponse response, CancellationToken cancellationToken) =>
        PostAsync(completion => new CompleteElicitationMessage(requestId, response, cancellationToken, completion));

    internal Task ApplyEventAsync(AgentEvent agentEvent) =>
        PostAsync(completion => new ApplyProviderEventMessage(agentEvent, completion));

    internal Task MarkFailedAsync(Exception exception) =>
        PostAsync(completion => new SessionTerminalMessage(exception, completion));

    /// <summary>
    /// Closes this member's mailbox to further posts. The read loop keeps draining every message already queued -
    /// dispatching each exactly as it would have run before retirement began - and completes on its own once the
    /// mailbox is empty, since a completed channel's reader still yields buffered items before <c>ReadAllAsync</c>
    /// ends. Waits for that loop, then for every operation a drained message started, so no detached provider call
    /// outlives the processor.
    /// </summary>
    internal async Task RetireAsync()
    {
        myMailbox.Writer.TryComplete();
        await myLoop.ConfigureAwait(false);
        Task[] inFlight;
        lock (myInFlightLock)
            inFlight = myInFlight.ToArray();
        await Task.WhenAll(inFlight).ConfigureAwait(false);
    }

    private async Task PostAsync(Func<TaskCompletionSource, MemberMessage> createMessage)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await myMailbox.Writer.WriteAsync(createMessage(completion)).ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }

    private async Task RunLoopAsync()
    {
        await foreach (var message in myMailbox.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            switch (message)
            {
                case ApplyProviderEventMessage applyProviderEvent:
                    RunInline(() => myApplyEvent(applyProviderEvent.Event), applyProviderEvent.Completion);
                    break;
                case SessionTerminalMessage sessionTerminal:
                    RunInline(() => myMarkFailed(sessionTerminal.Failure), sessionTerminal.Completion);
                    break;
                case SendPromptMessage sendPrompt:
                    DispatchDetached(token => myExecutePrompt(sendPrompt.Operation, token), sendPrompt.CancellationToken, sendPrompt.Completion);
                    break;
                case AbortMessage abort:
                    DispatchDetached(token => myExecuteAbort(token), abort.CancellationToken, abort.Completion);
                    break;
                case CompletePermissionMessage completePermission:
                    DispatchDetached(
                        token => myExecuteCompletePermission(completePermission.RequestId, completePermission.Response, token),
                        completePermission.CancellationToken,
                        completePermission.Completion);
                    break;
                case CompleteInputMessage completeInput:
                    DispatchDetached(
                        token => myExecuteCompleteInput(completeInput.RequestId, completeInput.Response, token),
                        completeInput.CancellationToken,
                        completeInput.Completion);
                    break;
                case CompleteElicitationMessage completeElicitation:
                    DispatchDetached(
                        token => myExecuteCompleteElicitation(completeElicitation.RequestId, completeElicitation.Response, token),
                        completeElicitation.CancellationToken,
                        completeElicitation.Completion);
                    break;
                case OperationOutcomeMessage outcome:
                    outcome.Apply();
                    break;
            }
        }
    }

    /// <summary>Runs a fast, provider-I/O-free message body inline, in order, on the read loop.</summary>
    private static void RunInline(Action action, TaskCompletionSource completion)
    {
        try
        {
            action();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    /// <summary>
    /// Starts a prompt, abort, or interaction-response operation's provider I/O without the read loop awaiting it,
    /// then reports its outcome back through this member's own mailbox as an <see cref="OperationOutcomeMessage"/>
    /// so applying that outcome - resolving <paramref name="completion"/> - still happens on the single read loop.
    /// </summary>
    private void DispatchDetached(Func<CancellationToken, Task> operation, CancellationToken cancellationToken, TaskCompletionSource completion)
    {
        var operationTask = RunDetachedAsync(operation, cancellationToken, completion);
        lock (myInFlightLock)
            myInFlight.Add(operationTask);
        _ = ObserveDetachedAsync(operationTask);
    }

    private async Task RunDetachedAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken, TaskCompletionSource completion)
    {
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            await PostOutcomeAsync(() => completion.TrySetResult()).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            await PostOutcomeAsync(() => completion.TrySetCanceled(exception.CancellationToken)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await PostOutcomeAsync(() => completion.TrySetException(exception)).ConfigureAwait(false);
        }
    }

    private async Task PostOutcomeAsync(Action apply)
    {
        var message = new OperationOutcomeMessage(apply);
        if (myMailbox.Writer.TryWrite(message))
        {
            return;
        }
        try
        {
            await myMailbox.Writer.WriteAsync(message).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // Retirement already drained the mailbox and stopped the read loop: apply the outcome directly so the
            // caller's completion still resolves.
            apply();
        }
    }

    private async Task ObserveDetachedAsync(Task operationTask)
    {
        try
        {
            await operationTask.ConfigureAwait(false);
        }
        catch
        {
            // The outcome, including any failure, was already reported through PostOutcomeAsync.
        }
        finally
        {
            lock (myInFlightLock)
                myInFlight.Remove(operationTask);
        }
    }
}
