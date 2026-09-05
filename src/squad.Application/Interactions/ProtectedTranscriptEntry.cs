namespace squad.Application.Interactions;

/// <summary>
/// Identifies a transcript entry that is being kept from truncation because a pending interaction is still
/// associated with it.
/// </summary>
internal readonly record struct ProtectedTranscriptEntry(string Role, int EntryIndex);
