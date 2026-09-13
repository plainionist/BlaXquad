using squad.Domain;

namespace squad.Ui.Abstractions;

/// <summary>Describes one sequenced mutation of a role transcript and its optional recovery announcement.</summary>
public sealed record TranscriptUpdate
{
    public SquadMemberId MemberId { get; init; }
    public long Sequence { get; init; }
    public TranscriptUpdateKind Kind { get; init; }
    public int EntryIndex { get; init; }
    public TranscriptEntry? Entry { get; init; }
    public string? Content { get; init; }
    public bool HasArchivedContent { get; init; }
    public long ContentStart { get; init; }
    public TranscriptAnnouncement? Announcement { get; init; }

    public TranscriptUpdate(
        SquadMemberId MemberId,
        long Sequence,
        TranscriptUpdateKind Kind,
        int EntryIndex,
        TranscriptEntry? Entry,
        string? Content,
        bool HasArchivedContent = false,
        long ContentStart = 0,
        TranscriptAnnouncement? Announcement = null)
    {
        Contract.Requires(Sequence >= 0, "Sequence must not be negative.");
        Contract.Requires(EntryIndex >= 0, "EntryIndex must not be negative.");
        Contract.Requires(ContentStart >= 0, "ContentStart must not be negative.");
        Contract.Requires(
            Kind != TranscriptUpdateKind.AppendContent || Content is not null,
            "AppendContent updates must supply Content.");
        Contract.Requires(
            Kind == TranscriptUpdateKind.AppendContent || Entry is not null,
            "AppendEntry and ReplaceEntry updates must supply Entry.");

        this.MemberId = MemberId;
        this.Sequence = Sequence;
        this.Kind = Kind;
        this.EntryIndex = EntryIndex;
        this.Entry = Entry;
        this.Content = Content;
        this.HasArchivedContent = HasArchivedContent;
        this.ContentStart = ContentStart;
        this.Announcement = Announcement;
    }
}
