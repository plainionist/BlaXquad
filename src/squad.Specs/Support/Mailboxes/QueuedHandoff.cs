namespace squad.Specs.Support.Mailboxes;

/// <summary>A queued handoff described in user terms: who sent it, who receives it, and its delivery instruction.
/// <paramref name="Recipient"/> is the single recipient header a durably delivered inbox copy carries - null for an
/// outbox, sent, or failed artifact, which still names every fan-out recipient only through <paramref name="Recipients"/>.</summary>
internal sealed record QueuedHandoff(
    string Sender,
    IReadOnlyList<string> Recipients,
    string Priority,
    string Type,
    string? Task,
    string? Message,
    string Payload,
    string? Recipient = null)
{
    /// <summary>Whether the payload instructs merging and processing the given committed change from the sender.</summary>
    public bool InstructsMergingCommit(string sender, string commit) =>
        Payload == $"merge_and_process {sender} {commit}";
}
