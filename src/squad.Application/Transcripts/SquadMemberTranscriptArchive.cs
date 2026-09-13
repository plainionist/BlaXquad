using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Application.Transcripts;

/// <summary>
/// One member's bounded archive handle for one squad generation. It binds the member identity and the owning
/// generation, so the member's transcript state addresses neither, reads every entry this member ever published
/// - including entries published by an earlier generation - and reserves entry indices and sequence numbers that
/// stay monotonic across generations. Publication is dropped once the owning generation is revoked, so a retired
/// member can never write again.
/// </summary>
internal sealed class SquadMemberTranscriptArchive
{
    private readonly TranscriptStore myStore;
    private readonly GenerationTranscriptArchive myGeneration;
    private readonly SquadMemberId myMember;

    internal SquadMemberTranscriptArchive(TranscriptStore store, GenerationTranscriptArchive generation, SquadMemberId member)
    {
        myStore = store;
        myGeneration = generation;
        myMember = member;
    }

    internal TranscriptRetentionOptions RetentionOptions => myStore.RetentionOptions;

    internal int ReserveEntryIndex() => myStore.ReserveEntryIndex(myMember);

    internal long AdvanceSequence() => myStore.AdvanceSequence(myMember);

    internal long CurrentSequence() => myStore.CurrentSequence(myMember);

    internal void Apply(TranscriptUpdate update)
    {

        if (!myGeneration.IsLive)
        {
            return;
        }

        myStore.Archive.Apply(update);
    }

    internal IReadOnlyList<IndexedTranscriptEntry> ReadPage(int beforeIndex, int maxEntries) =>
        myStore.Archive.ReadPage(myMember, beforeIndex, maxEntries);

    internal bool HasEntriesBefore(int beforeIndex) =>
        myStore.Archive.HasEntriesBefore(myMember, beforeIndex);

    internal bool HasMoreContent(int entryIndex, long retainedContentStart) =>
        myStore.Archive.HasMoreContent(myMember, entryIndex, retainedContentStart);

    internal RoleArchivedTranscriptEntry ReadEntry(int entryIndex, long sequence) =>
        myStore.Archive.ReadEntry(myMember, entryIndex, sequence);

    internal bool HasEntriesOutside(IReadOnlyCollection<int> includedIndices) =>
        myStore.Archive.HasEntriesOutside(myMember, includedIndices);

    internal bool WasTruncated() => myStore.Archive.WasTruncated(myMember);
}
