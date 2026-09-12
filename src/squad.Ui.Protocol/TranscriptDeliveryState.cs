namespace squad.Ui.Protocol;

/// <summary>
/// One member's transcript delivery state for one UI connection: the durable delivered and synchronized sequence
/// cursors, which only ever advance once observed, and any not-yet-consumed recovery position requested while a
/// publish cycle was pending. A cursor is <see langword="null"/> until this member's first incremental delivery or
/// synchronization - that absence is never equivalent to an observed cursor at sequence 0, so a member known only
/// through a requested recovery position is never mistaken for one already delivered to or synchronized. Repeated
/// requests received before the next publish cycle are merged by keeping the earliest (minimum) visual and
/// announcement sequence, so a later, narrower request can never cause recovery to skip content an earlier request
/// still needed.
/// </summary>
internal sealed record TranscriptDeliveryState(
    long? DeliveredSequence,
    long? SynchronizedSequence,
    TranscriptSynchronizationPosition? RequestedPosition)
{
    internal static readonly TranscriptDeliveryState Initial = new(null, null, null);

    /// <summary>Advances the delivered cursor for one incremental transcript update.</summary>
    internal TranscriptDeliveryState WithDelivered(long sequence)
    {
        Contract.Requires(
            DeliveredSequence is not { } previous || sequence >= previous,
            "Delivered transcript sequence must never move backward.");
        return this with { DeliveredSequence = sequence };
    }

    /// <summary>Advances both cursors together for a synchronization snapshot, which also delivers.</summary>
    internal TranscriptDeliveryState WithSynchronized(long sequence)
    {
        Contract.Requires(
            DeliveredSequence is not { } previousDelivered || sequence >= previousDelivered,
            "Delivered transcript sequence must never move backward.");
        Contract.Requires(
            SynchronizedSequence is not { } previousSynchronized || sequence >= previousSynchronized,
            "Synchronized transcript sequence must never move backward.");
        return this with { DeliveredSequence = sequence, SynchronizedSequence = sequence };
    }

    /// <summary>Merges one more requested recovery position into any not-yet-consumed request.</summary>
    internal TranscriptDeliveryState WithRequestedPosition(TranscriptSynchronizationPosition position)
    {
        var merged = RequestedPosition is { } existing
            ? new TranscriptSynchronizationPosition(
                Math.Min(existing.VisualSequence, position.VisualSequence),
                Math.Min(existing.AnnouncementSequence, position.AnnouncementSequence))
            : position;
        return this with { RequestedPosition = merged };
    }

    /// <summary>Clears the transient recovery request a publish cycle consumed, preserving both durable cursors.</summary>
    internal TranscriptDeliveryState ConsumingRequestedPosition() =>
        RequestedPosition is null ? this : this with { RequestedPosition = null };
}
