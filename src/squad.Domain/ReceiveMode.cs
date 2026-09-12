namespace squad.Domain;

/// <summary>How a configured squad member accepts queued handoffs: one at a time (<see cref="Task"/>) or every
/// currently queued handoff at the highest priority together (<see cref="Batch"/>). The stable JSON and command
/// spellings ("task"/"batch") are mapped to and from this enum at configuration and command boundaries.</summary>
public enum ReceiveMode
{
    Task,
    Batch
}
