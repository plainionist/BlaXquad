using global::squad.Ui.Abstractions;

namespace squad.Photino;

public sealed class TranscriptAnnouncementJournal
{
    private readonly int myMaxEntriesPerRole;
    private readonly int myMaxCharactersPerRole;
    private readonly Dictionary<string, RoleJournal> myRoles = new(StringComparer.Ordinal);
    private readonly object myStateLock = new();

    public TranscriptAnnouncementJournal(
        int maxEntriesPerRole,
        int maxCharactersPerRole)
    {
        myMaxEntriesPerRole = maxEntriesPerRole;
        myMaxCharactersPerRole = maxCharactersPerRole;
    }

    public void Add(TranscriptUpdate update)
    {
        lock (myStateLock)
        {
            if (!myRoles.TryGetValue(update.Role, out var journal))
            {
                journal = new RoleJournal();
                myRoles.Add(update.Role, journal);
            }

            journal.Entries.Enqueue(new JournalEntry(update.Sequence, update.Announcement));
            journal.CharacterCount += update.Announcement?.Content.Length ?? 0;
            while (journal.Entries.Count > myMaxEntriesPerRole
                || journal.CharacterCount > myMaxCharactersPerRole)
            {
                var removed = journal.Entries.Dequeue();
                journal.CharacterCount -= removed.Announcement?.Content.Length ?? 0;
                if (removed.Announcement is not null)
                    journal.AnnouncementDiscardedThroughSequence = removed.Sequence;
            }
        }
    }

    public TranscriptRecoveryAnnouncement Read(
        string role,
        long afterSequence,
        long throughSequence)
    {
        lock (myStateLock)
        {
            if (!myRoles.TryGetValue(role, out var journal))
                return new(afterSequence, throughSequence, [], false);

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
    }
}



