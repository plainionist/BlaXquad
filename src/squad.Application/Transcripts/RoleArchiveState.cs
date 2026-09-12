namespace squad.Application.Transcripts;

/// <summary>One role's archived transcript index: its entries in append order, keyed by entry index, plus a sticky
/// flag recording whether eviction or content truncation has ever discarded data for this role. The flag never
/// resets once set - a role that was truncated in the past stays reported as truncated even after later appends or
/// evictions that do not themselves truncate anything.</summary>
internal sealed class RoleArchiveState
{
    internal SortedDictionary<int, ArchivedEntryState> Entries { get; } = [];
    internal bool Truncated { get; private set; }

    internal void MarkTruncated() => Truncated = true;
}
