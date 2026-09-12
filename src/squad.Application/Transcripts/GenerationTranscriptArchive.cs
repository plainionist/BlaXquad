using squad.Domain;

namespace squad.Application.Transcripts;

/// <summary>
/// One squad generation's bounded handle into the process-lifetime <see cref="TranscriptStore"/>. It carries the
/// generation identity and its liveness; revoking it at retirement prevents every member of that generation from
/// publishing further transcript content without discarding the history they already published.
/// </summary>
internal sealed class GenerationTranscriptArchive
{
    private readonly TranscriptStore myStore;
    private volatile bool myRevoked;

    internal GenerationTranscriptArchive(TranscriptStore store, SquadGenerationId generation)
    {
        myStore = store;
        Generation = generation;
    }

    internal SquadGenerationId Generation { get; }

    internal bool IsLive => !myRevoked;

    /// <summary>Opens the member-bound handle through which one member publishes and reads its own history.</summary>
    internal SquadMemberTranscriptArchive OpenMember(SquadMemberId member) => new(myStore, this, member);

    /// <summary>Permanently closes publication for this generation. Idempotent.</summary>
    internal void Revoke() => myRevoked = true;
}
