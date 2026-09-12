using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>Maps authoritative transcript models to the wire payload shape consumed by the UI.</summary>
internal static class TranscriptProtocol
{
    public static object CreateSynchronizationPayload(
        IReadOnlyList<RoleTranscriptSnapshot> transcriptSnapshot,
        IReadOnlyDictionary<SquadMemberId, TranscriptRecoveryAnnouncement>? recoveryAnnouncements = null,
        bool recovery = false) => new
        {
            recovery,
            roles = transcriptSnapshot.Select(role => new
            {
                role = role.MemberId.Value,
                sequence = role.Sequence,
                entries = role.Entries.Select(CreateIndexedEntryPayload),
                hasMore = role.HasMore,
                historyTruncated = role.HistoryTruncated,
                announcementAfter = recoveryAnnouncements?.TryGetValue(
                    role.MemberId,
                    out var interval) == true
                        ? interval.AfterSequence
                        : (long?)null,
                announcementThrough = recoveryAnnouncements?.TryGetValue(
                    role.MemberId,
                    out interval) == true
                        ? interval.ThroughSequence
                        : (long?)null,
                announcement = recoveryAnnouncements?.TryGetValue(
                    role.MemberId,
                    out var announcement) == true
                    && (announcement.Truncated || announcement.Fragments.Count > 0)
                        ? CreateRecoveryAnnouncementPayload(announcement)
                        : null,
            }),
        };

    public static object CreateUpdatePayload(TranscriptUpdate update) => new
    {
        role = update.MemberId.Value,
        sequence = update.Sequence,
        operation = update.Kind switch
        {
            TranscriptUpdateKind.AppendEntry => "append",
            TranscriptUpdateKind.AppendContent => "append-content",
            TranscriptUpdateKind.ReplaceEntry => "replace",
            _ => throw new ArgumentOutOfRangeException(nameof(update.Kind)),
        },
        entryIndex = update.EntryIndex,
        entry = update.Entry is null
            ? null
            : CreateEntryPayload(
                update.Entry,
                update.HasArchivedContent,
                update.ContentStart),
        content = update.Content,
        announcement = update.Announcement is null
            ? null
            : CreateAnnouncementPayload(update.Announcement),
    };

    public static object CreatePagePayload(RoleTranscriptPage page) => new
    {
        role = page.MemberId.Value,
        entries = page.Entries.Select(CreateIndexedEntryPayload),
        hasMore = page.HasMore,
        historyTruncated = page.HistoryTruncated,
    };

    public static object CreateArchivedEntryPayload(RoleArchivedTranscriptEntry archivedEntry) => new
    {
        role = archivedEntry.MemberId.Value,
        sequence = archivedEntry.Sequence,
        entryIndex = archivedEntry.EntryIndex,
        entry = archivedEntry.Entry is null
            ? null
            : CreateEntryPayload(archivedEntry.Entry),
        contentTruncated = archivedEntry.ContentTruncated,
        totalContentCharacters = archivedEntry.TotalContentCharacters,
        archivedPrefixCharacters = archivedEntry.ArchivedPrefixCharacters,
    };

    private static object CreateEntryPayload(
        TranscriptEntry entry,
        bool hasArchivedContent = false,
        long contentStart = 0) => new
    {
        occurredAt = entry.OccurredAt,
        source = entry.Source,
        content = entry.Content,
        hasArchivedContent,
        contentStart,
    };

    private static object CreateIndexedEntryPayload(IndexedTranscriptEntry entry) => new
    {
        entryIndex = entry.EntryIndex,
        occurredAt = entry.Entry.OccurredAt,
        source = entry.Entry.Source,
        content = entry.Entry.Content,
        hasArchivedContent = entry.HasArchivedContent,
        contentStart = entry.ContentStart,
    };

    private static object CreateRecoveryAnnouncementPayload(
        TranscriptRecoveryAnnouncement announcement) => new
        {
            fragments = announcement.Fragments.Select(fragment => new
            {
                sequence = fragment.Sequence,
                entryIndex = fragment.Announcement.EntryIndex,
                operation = fragment.Announcement.Kind switch
                {
                    TranscriptAnnouncementKind.AppendEntry => "append",
                    TranscriptAnnouncementKind.AppendContent => "append-content",
                    TranscriptAnnouncementKind.Replace => "replace",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(fragment.Announcement.Kind)),
                },
                content = fragment.Announcement.Content,
                truncated = fragment.Announcement.Truncated,
            }),
            truncated = announcement.Truncated,
        };

    private static object CreateAnnouncementPayload(
        TranscriptAnnouncement announcement) => new
        {
            entryIndex = announcement.EntryIndex,
            operation = announcement.Kind switch
            {
                TranscriptAnnouncementKind.AppendEntry => "append",
                TranscriptAnnouncementKind.AppendContent => "append-content",
                TranscriptAnnouncementKind.Replace => "replace",
                _ => throw new ArgumentOutOfRangeException(nameof(announcement.Kind)),
            },
            content = announcement.Content,
            truncated = announcement.Truncated,
        };
}


