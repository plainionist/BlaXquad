namespace squad.Application.Transcripts;

/// <summary>One archived transcript entry's retained on-disk length, its original pre-truncation length, and
/// whether storing it exceeded the configured per-entry character limit. Neither length is ever negative, and
/// <see cref="ContentTruncated"/> is re-derived on every append or replace rather than staying sticky, so a later
/// replace that fits within the limit correctly reports the entry as no longer truncated.</summary>
internal sealed record ArchivedEntryState
{
    internal int RetainedLength { get; }
    internal long TotalLength { get; }
    internal bool ContentTruncated { get; }

    internal ArchivedEntryState(int retainedLength, long totalLength, bool contentTruncated)
    {
        Contract.Requires(retainedLength >= 0, "Retained entry length must not be negative.");
        Contract.Requires(totalLength >= 0, "Total entry length must not be negative.");
        RetainedLength = retainedLength;
        TotalLength = totalLength;
        ContentTruncated = contentTruncated;
    }

    internal ArchivedEntryState WithRetainedLength(int retainedLength) =>
        new(retainedLength, TotalLength, ContentTruncated);

    internal ArchivedEntryState WithAddedTotalLength(long additionalLength) =>
        new(RetainedLength, TotalLength + additionalLength, ContentTruncated);

    internal ArchivedEntryState WithContentTruncated() =>
        new(RetainedLength, TotalLength, true);
}
