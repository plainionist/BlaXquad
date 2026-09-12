namespace squad.Application;

/// <summary>
/// One member's pending or in-flight interaction, keyed only by <see cref="InteractionRequestId"/> in
/// <see cref="SquadMemberAggregate"/>. Every request kind - permission, input, and elicitation - shares this same
/// per-request lifecycle: registered while awaiting a local decision (<see cref="Pending{TRequest}"/>), transitioned
/// while a provider response is in flight (<see cref="Responding{TRequest}"/>), and, only across headquarters
/// shutdown, retained solely to keep its transcript entry protected through the generation's retirement
/// (<see cref="RetainedForRetirement"/>). Each variant carries exactly the request kind and transcript-entry index
/// valid for its phase, never a nullable request or a freely combinable flag.
/// </summary>
internal abstract record MemberInteractionState(int ProtectedTranscriptEntryIndex)
{
    /// <summary>
    /// Attempts to return the pending counterpart of this state, preserving its request and transcript entry.
    /// Only a <see cref="Responding{TRequest}"/> state supports this transition; every other variant no-ops,
    /// because by the time a recoverable response failure observes them, a concurrent abort or headquarters
    /// shutdown has already taken this interaction out of responding - there is nothing left to restore.
    /// </summary>
    internal virtual bool TryRestore(out MemberInteractionState restored)
    {
        restored = this;
        return false;
    }

    internal sealed record Pending<TRequest>(TRequest Request, int ProtectedTranscriptEntryIndex)
        : MemberInteractionState(ProtectedTranscriptEntryIndex);

    internal sealed record Responding<TRequest>(TRequest Request, int ProtectedTranscriptEntryIndex)
        : MemberInteractionState(ProtectedTranscriptEntryIndex)
    {
        internal override bool TryRestore(out MemberInteractionState restored)
        {
            restored = new Pending<TRequest>(Request, ProtectedTranscriptEntryIndex);
            return true;
        }
    }

    /// <summary>
    /// A live interaction whose request kind headquarters shutdown has already cleared from the published
    /// pending-interaction view, but whose transcript entry deliberately remains protected for the rest of the
    /// generation's retirement.
    /// </summary>
    internal sealed record RetainedForRetirement(int ProtectedTranscriptEntryIndex)
        : MemberInteractionState(ProtectedTranscriptEntryIndex);
}
