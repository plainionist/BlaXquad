using squad.Domain;

namespace squad.Handoffs;

/// <summary>
/// The one durable JSON representation of a handoff, shared by the role CLI, Headquarters delivery, and role queue
/// commands. <see cref="Kind"/> selects exactly one of <see cref="GitHandoff"/> or <see cref="Note"/>; the other
/// must be absent. Recipients and priority use native JSON types rather than encoded delimiter-separated strings.
/// A process launch is the format boundary, so no persisted handoff needs to cross executable versions and the
/// document carries no schema-version field. <see cref="Validate"/> must be called after deserialization and before
/// serialization so no producer or consumer can persist or act on an incomplete or self-contradictory document.
/// </summary>
public sealed record HandoffDocument
{
    /// <summary>The suffix identifying a durable handoff artifact as JSON, distinct from a legacy ".handoff" file.</summary>
    public const string FileSuffix = ".handoff.json";

    /// <summary>The maximum length the CLI enforces for a human-authored <see cref="GitHandoffData.Task"/> or
    /// <see cref="NoteData.Message"/>, re-enforced here so a persisted document can never bypass it.</summary>
    private const int MaxTextLength = 80;

    public required HandoffId Id { get; init; }
    public required SquadMemberId From { get; init; }
    public required IReadOnlyList<SquadMemberId> To { get; init; }
    public SquadMemberId? Recipient { get; init; }
    public required HandoffPriority Priority { get; init; }
    public required HandoffKind Kind { get; init; }
    public GitHandoffData? GitHandoff { get; init; }
    public NoteData? Note { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? EnqueuedAt { get; init; }
    public DateTimeOffset? DequeuedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Derives the recipient-facing payload instruction from typed fields instead of persisting the same
    /// information again in a second embedded mini-language.</summary>
    public string RenderPayload() => Kind switch
    {
        HandoffKind.GitHandoff => $"merge_and_process {From} {GitHandoff!.Commit}",
        HandoffKind.Note => Note!.Message,
        _ => throw new InvalidDataException($"unknown handoff kind {Kind}"),
    };

    /// <summary>Rejects a missing or empty recipient list, an empty or overlong <see cref="GitHandoffData.Task"/> or
    /// <see cref="NoteData.Message"/>, and any kind/variant pairing other than exactly the variant matching
    /// <see cref="Kind"/>.</summary>
    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(From.Value))
        {
            errors.Add("missing from");
        }
        if (To is null || To.Count == 0 || To.Any(recipient => string.IsNullOrWhiteSpace(recipient.Value)))
        {
            errors.Add("missing or empty to");
        }
        if (CreatedAt == default)
        {
            errors.Add("missing createdAt");
        }

        switch (Kind)
        {
            case HandoffKind.GitHandoff:
                ValidateVariant(errors, "git_handoff", GitHandoff, Note);
                if (GitHandoff is not null)
                {
                    if (string.IsNullOrWhiteSpace(GitHandoff.Task))
                    {
                        errors.Add("missing gitHandoff.task");
                    }
                    else if (GitHandoff.Task.Length > MaxTextLength)
                    {
                        errors.Add($"gitHandoff.task must be no longer than {MaxTextLength} characters; got {GitHandoff.Task.Length}");
                    }
                    if (GitHandoff.Commit is null)
                    {
                        errors.Add("missing gitHandoff.commit");
                    }
                }
                break;
            case HandoffKind.Note:
                ValidateVariant(errors, "note", Note, GitHandoff);
                if (Note is not null)
                {
                    if (string.IsNullOrWhiteSpace(Note.Message))
                    {
                        errors.Add("missing note.message");
                    }
                    else if (Note.Message.Length > MaxTextLength)
                    {
                        errors.Add($"note.message must be no longer than {MaxTextLength} characters; got {Note.Message.Length}");
                    }
                }
                break;
            default:
                errors.Add($"unknown handoff kind {Kind}");
                break;
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join("; ", errors));
        }
    }

    private static void ValidateVariant(List<string> errors, string kindLabel, object? expected, object? unexpected)
    {
        if (expected is null)
        {
            errors.Add($"kind '{kindLabel}' requires its matching variant data");
        }
        if (unexpected is not null)
        {
            errors.Add($"kind '{kindLabel}' must not carry the other variant's data");
        }
    }
}
