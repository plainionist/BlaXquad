namespace squad.Ui.Abstractions;

public interface ITranscriptUi
{
    event Action<TranscriptUpdate>? TranscriptChanged;
    IReadOnlyList<RoleTranscriptSnapshot> CreateTranscriptSnapshot(int maxEntriesPerRole);
    RoleTranscriptPage CreateTranscriptPage(string role, int beforeIndex, int maxEntries);
    RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(string role, int entryIndex);
}



