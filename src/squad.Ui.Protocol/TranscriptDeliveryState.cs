namespace squad.Ui.Protocol;

/// <summary>
/// One member's transcript delivery state for one UI connection: the durable delivered and synchronized sequence
/// cursors, which only ever advance, and any not-yet-consumed recovery position requested while a publish cycle
/// was pending. Repeated requests received before the next publish cycle are merged by keeping the earliest
/// (minimum) visual and announcement sequence, so a later, narrower request can never cause recovery to skip
/// content an earlier request still needed.
/// </summary>
internal sealed record TranscriptDeliveryState(
    long DeliveredSequence,
    long SynchronizedSequence,
    TranscriptSynchronizationPosition? RequestedPosition)
{
    internal static readonly TranscriptDeliveryState Initial = new(0, 0, null);

    /// <summary>Advances the delivered cursor for one incremental transcript update.</summary>
    internal TranscriptDeliveryState WithDelivered(long sequence)
    {
        Contract.Requires(sequence >= DeliveredSequence, "Delivered transcript sequence must never move backward.");
        return this with { DeliveredSequence = sequence };
    }

    /// <summary>Advances both cursors together for a synchronization snapshot, which also delivers.</summary>
    internal TranscriptDeliveryState WithSynchronized(long sequence)
    {
        Contract.Requires(sequence >= DeliveredSequence, "Delivered transcript sequence must never move backward.");
        Contract.Requires(sequence >= SynchronizedSequence, "Synchronized transcript sequence must never move backward.");
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
