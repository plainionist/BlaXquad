using squad.Domain;
using squad.Handoffs;

namespace squad.Specs.Support.Mailboxes;

/// <summary>A queued handoff described in user terms: who sent it, who receives it, and its delivery instruction.
/// <paramref name="Recipient"/> is the single recipient header a durably delivered inbox copy carries - null for an
/// outbox, sent, or failed artifact, which still names every fan-out recipient only through <paramref name="Recipients"/>.
/// <paramref name="CreatedAt"/>, <paramref name="EnqueuedAt"/>, <paramref name="DequeuedAt"/>, and
/// <paramref name="CompletedAt"/> are the document's lifecycle timestamps, present only once the corresponding
/// transition (creation, delivery, claim, or completion) has happened to this artifact.</summary>
internal sealed record QueuedHandoff(
    HandoffId Id,
    SquadMemberId Sender,
    IReadOnlyList<SquadMemberId> Recipients,
    HandoffPriority Priority,
    HandoffKind Type,
    string? Task,
    string? Message,
    string Payload,
    SquadMemberId? Recipient,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EnqueuedAt,
    DateTimeOffset? DequeuedAt,
    DateTimeOffset? CompletedAt)
{
    /// <summary>Whether the payload instructs merging and processing the given committed change from the sender.</summary>
    public bool InstructsMergingCommit(string sender, string commit) =>
        Payload == $"merge_and_process {sender} {commit}";
}
