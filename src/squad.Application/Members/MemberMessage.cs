using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Domain;

namespace squad.Application.Members;

/// <summary>
/// The typed messages one member's <see cref="MemberProcessor"/> mailbox accepts. Each message carries the
/// <see cref="TaskCompletionSource"/> its sender awaits, so the sender observes success, failure, or cancellation
/// exactly as it did before typed routing replaced the opaque command delegates - only the routing and the mutation
/// ownership changed. A prompt, abort, or interaction-response message performs its own provider I/O outside the
/// processor's read loop and reports its outcome back through <see cref="OperationOutcomeMessage"/>, carrying this
/// member's current generation and operation identity, so the loop can drop a mutation that no longer matches this
/// member's active operation instead of reopening canceled or terminal work.
/// </summary>
internal abstract record MemberMessage;

/// <summary>Distinguishes the two typed prompt operations a member can receive without an opaque delegate.</summary>
internal enum PromptKind
{
    Prompt,
    Harness,
}

internal sealed record SendPromptMessage(
    PromptKind Kind,
    string Prompt,
    CancellationToken CancellationToken,
    TaskCompletionSource Completion) : MemberMessage;

/// <summary>Aborts this member's active work. Leader/follower coalescing happens inside the processor.</summary>
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
/// Applies the mutation a prompt, abort, or interaction-response operation performs once its async admission gates
/// (prompt lease, abort wait, operation lease) have passed but before its provider I/O begins, carrying the
/// squad generation the processor belongs to, the member it belongs to, and the identity of the operation
/// starting. The
/// processor's read loop applies <see cref="Apply"/> inline and only after confirming <see cref="OperationId"/>
/// still names this member's active operation - dropping a start mutation for an operation a later dispatch already
/// superseded before this one's gates cleared - then always resolves <see cref="Applied"/> so the awaiting detached
/// operation can proceed (having its mutation applied) or return early (having had it correctly dropped).
/// </summary>
internal sealed record OperationStartingMessage(
    SquadGenerationId Generation,
    SquadMemberId Member,
    Guid OperationId,
    Action Apply,
    TaskCompletionSource Applied) : MemberMessage;

/// <summary>
/// Reports the outcome of a prompt, abort, or interaction-response operation once its detached provider I/O has
/// concluded, carrying the processor's generation, the member it belongs to, and the identity of the operation it
/// completes. The processor's read loop applies <see cref="ApplyMutation"/> inline, and only when
/// <see cref="Unconditional"/> is set or <see cref="OperationId"/> still names this member's active operation, so a
/// completion superseded by a newer operation, an intervening abort, or a terminal failure can never reapply stale
/// mutations - while <see cref="ResolveCompletion"/> always runs, so the original caller still observes a definitive
/// result. Only an abort's own cleanup - which must land regardless of what a later operation, dispatched while the
/// abort's provider call was still in flight, has since superseded - sets <see cref="Unconditional"/>.
/// </summary>
internal sealed record OperationOutcomeMessage(
    SquadGenerationId Generation,
    SquadMemberId Member,
    Guid OperationId,
    Action? ApplyMutation,
    bool Unconditional,
    Action ResolveCompletion) : MemberMessage;

/// <summary>
/// Marks the point, once read, before which every command message queued when retirement began has already been
/// read - and so every detached operation those messages will ever start has already been dispatched - letting
/// <see cref="MemberProcessor.RetireAsync"/> safely wait out exactly those operations before closing the mailbox.
/// </summary>
internal sealed record RetirementSentinelMessage(TaskCompletionSource Drained) : MemberMessage;
