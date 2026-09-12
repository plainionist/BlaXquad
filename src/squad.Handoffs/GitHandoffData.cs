namespace squad.Handoffs;

/// <summary>Kind-specific data for a Git handoff: a stable task name and the commit it communicates.</summary>
public sealed record GitHandoffData(string Task, GitCommitId Commit);
