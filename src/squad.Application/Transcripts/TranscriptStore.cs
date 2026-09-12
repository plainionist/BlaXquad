using squad.Domain;

namespace squad.Application.Transcripts;

/// <summary>
/// The process-lifetime owner of the transcript archive, its retention policy, and each member's monotonic
/// publication identity. Headquarters constructs exactly one store and disposes it when the process releases its
/// resources, so archived history and per-member entry/sequence numbering survive any single squad generation.
/// A generation never reaches the archive directly: it opens a bounded, revocable handle through
/// <see cref="OpenGeneration"/>.
/// </summary>
public sealed class TranscriptStore : IDisposable
{
    private readonly TranscriptRetentionOptions myRetentionOptions = new();
    private readonly TranscriptArchive myArchive;
    private readonly object myPublicationLock = new();
    private readonly Dictionary<SquadMemberId, SquadMemberPublicationIdentity> myPublicationIdentities = [];

    public TranscriptStore()
    {
        myArchive = new TranscriptArchive(myRetentionOptions);
    }

    /// <summary>
    /// Opens a handle bound to one squad generation. Revoking it stops that generation's members from publishing
    /// further archive content while every entry they already published remains readable.
    /// </summary>
    internal GenerationTranscriptArchive OpenGeneration(SquadGenerationId generation) =>
        new(this, generation);

    internal TranscriptRetentionOptions RetentionOptions => myRetentionOptions;

    internal TranscriptArchive Archive => myArchive;

    /// <summary>Reserves the next transcript entry index for a member, continuing across squad generations.</summary>
    internal int ReserveEntryIndex(SquadMemberId member)
    {
        lock (myPublicationLock)
            return GetIdentity(member).NextEntryIndex++;
    }

    /// <summary>Advances a member's transcript sequence, continuing across squad generations.</summary>
    internal long AdvanceSequence(SquadMemberId member)
    {
        lock (myPublicationLock)
            return ++GetIdentity(member).Sequence;
    }

    /// <summary>The sequence through which a member's published transcript is current.</summary>
    internal long CurrentSequence(SquadMemberId member)
    {
        lock (myPublicationLock)
            return GetIdentity(member).Sequence;
    }

    public void Dispose() => myArchive.Dispose();

    private SquadMemberPublicationIdentity GetIdentity(SquadMemberId member)
    {
        if (!myPublicationIdentities.TryGetValue(member, out var identity))
        {
            myPublicationIdentities[member] = identity = new SquadMemberPublicationIdentity();
        }
        return identity;
    }

    private sealed class SquadMemberPublicationIdentity
    {
        public int NextEntryIndex;
        public long Sequence;
    }
}
