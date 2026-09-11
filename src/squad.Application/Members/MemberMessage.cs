using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Application.Members;

/// <summary>
/// The typed messages one member's <see cref="MemberProcessor"/> mailbox accepts. Each message carries the
/// <see cref="TaskCompletionSource"/> its sender awaits, so the sender observes success, failure, or cancellation
/// exactly as it did through the removed opaque command delegates - only the routing changed. A prompt, harness,
/// abort, or interaction-response message performs its own provider I/O outside the processor's read loop and
/// reports its outcome back through <see cref="OperationOutcomeMessage"/> instead of being awaited inline.
/// </summary>
internal abstract record MemberMessage;

/// <summary>Sends a prompt or harness instruction. <see cref="Operation"/> distinguishes the two wire commands.</summary>
internal sealed record SendPromptMessage(
    Func<IAgentSession, CancellationToken, Task> Operation,
    CancellationToken CancellationToken,
    TaskCompletionSource Completion) : MemberMessage;

/// <summary>Aborts this member's active work. Leader/follower coalescing happens before this message is posted.</summary>
internal sealed record AbortMessage(
    CancellationToken CancellationToken,
    TaskCompletionSource Completion) : MemberMessage;

internal sealed record CompletePermissionMessage(
    string RequestId,
    AgentPermissionResponse Response,
    CancellationToken CancellationToken,
    TaskCompletionSource Completion) : MemberMessage;

internal sealed record CompleteInputMessage(
    string RequestId,
    AgentInputResponse Response,
    CancellationToken CancellationToken,
    TaskCompletionSource Completion) : MemberMessage;

internal sealed record CompleteElicitationMessage(
    string RequestId,
    AgentElicitationResponse Response,
    CancellationToken CancellationToken,
    TaskCompletionSource Completion) : MemberMessage;

/// <summary>Applies one projected provider event. Never awaits provider I/O, so the read loop runs it inline.</summary>
internal sealed record ApplyProviderEventMessage(
    AgentEvent Event,
    TaskCompletionSource Completion) : MemberMessage;

/// <summary>Marks this member permanently failed. Never awaits provider I/O, so the read loop runs it inline.</summary>
internal sealed record SessionTerminalMessage(
    Exception Failure,
    TaskCompletionSource Completion) : MemberMessage;

/// <summary>
/// Reports the outcome of a prompt, abort, or interaction-response operation once its detached provider I/O has
/// concluded. The processor's read loop applies <see cref="Apply"/> inline - the sole place that operation's
/// success/failure mutation and completion-source resolution happen - so a slow provider call can never be awaited
/// by the loop itself while still funnelling every mutation through one ordered path.
/// </summary>
internal sealed record OperationOutcomeMessage(Action Apply) : MemberMessage;
