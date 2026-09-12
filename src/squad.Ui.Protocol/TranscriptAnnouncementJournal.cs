using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>
/// Retains a bounded per-role sequence of incremental transcript announcements for reconnect recovery. Reads report
/// when the requested interval predates retained announcement content.
/// </summary>
internal sealed class TranscriptAnnouncementJournal
{
    private readonly int myMaxEntriesPerRole;
    private readonly int myMaxCharactersPerRole;
    private readonly Dictionary<SquadMemberId, RoleJournal> myRoles = [];
    private readonly object myStateLock = new();

    public TranscriptAnnouncementJournal(
        int maxEntriesPerRole,
        int maxCharactersPerRole)
    {
        Contract.Requires(maxEntriesPerRole > 0, "maxEntriesPerRole must be positive.");
        Contract.Requires(maxCharactersPerRole > 0, "maxCharactersPerRole must be positive.");
        myMaxEntriesPerRole = maxEntriesPerRole;
        myMaxCharactersPerRole = maxCharactersPerRole;
    }

    public void Add(TranscriptUpdate update)
    {
        lock (myStateLock)
        {
            if (!myRoles.TryGetValue(update.MemberId, out var journal))
            {
                journal = new RoleJournal();
                myRoles.Add(update.MemberId, journal);
            }

            Contract.Invariant(
                update.Sequence > journal.LastSequence,
                "Transcript announcement sequence must increase monotonically per role.");
            journal.LastSequence = update.Sequence;

            journal.Entries.Enqueue(new JournalEntry(update.Sequence, update.Announcement));
            journal.CharacterCount += update.Announcement?.Content.Length ?? 0;
            while (journal.Entries.Count > myMaxEntriesPerRole
                || journal.CharacterCount > myMaxCharactersPerRole)
            {
                var removed = journal.Entries.Dequeue();
                journal.CharacterCount -= removed.Announcement?.Content.Length ?? 0;
                if (removed.Announcement is not null)
                {
                    journal.AnnouncementDiscardedThroughSequence = removed.Sequence;
                }
            }
            Contract.Invariant(
                journal.CharacterCount >= 0,
                "Retained announcement character count must not become negative.");
        }
    }

    /// <summary>
    /// Returns retained announcements after <paramref name="afterSequence"/> through
    /// <paramref name="throughSequence"/> inclusive, and marks the result truncated when required earlier content
    /// has been discarded.
    /// </summary>
    public TranscriptRecoveryAnnouncement Read(
        SquadMemberId memberId,
        long afterSequence,
        long throughSequence)
    {
        lock (myStateLock)
        {
            if (!myRoles.TryGetValue(memberId, out var journal))
            {
                return new(afterSequence, throughSequence, [], false);
            }

            var fragments = journal.Entries
                .Where(entry =>
                    entry.Sequence > afterSequence
                    && entry.Sequence <= throughSequence
                    && entry.Announcement is not null)
                .Select(entry => new SequencedTranscriptAnnouncement(
                    entry.Sequence,
                    entry.Announcement!))
                .ToArray();
            return new(
                afterSequence,
                throughSequence,
                fragments,
                afterSequence < journal.AnnouncementDiscardedThroughSequence);
        }
    }

    private sealed record JournalEntry(
        long Sequence,
        TranscriptAnnouncement? Announcement);

    private sealed class RoleJournal
    {
        internal Queue<JournalEntry> Entries { get; } = new();
        internal int CharacterCount { get; set; }
        internal long AnnouncementDiscardedThroughSequence { get; set; }
        internal long LastSequence { get; set; }
    }
}
