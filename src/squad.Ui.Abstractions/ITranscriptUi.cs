namespace squad.Ui.Abstractions;

/// <summary>Provides sequenced transcript changes plus bounded snapshots and archived-history retrieval.</summary>
public interface ITranscriptUi
{
    /// <summary>Publishes each committed transcript mutation in sequence order.</summary>
    event Action<TranscriptUpdate>? TranscriptChanged;
    /// <summary>Returns each role's latest retained entries and the sequence through which they are current.</summary>
    IReadOnlyList<RoleTranscriptSnapshot> CreateTranscriptSnapshot(int maxEntriesPerRole);
    /// <summary>Reads archived entries strictly before <paramref name="beforeIndex"/>, ordered oldest to newest.</summary>
    RoleTranscriptPage CreateTranscriptPage(string role, int beforeIndex, int maxEntries);
    RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(string role, int entryIndex);
}

