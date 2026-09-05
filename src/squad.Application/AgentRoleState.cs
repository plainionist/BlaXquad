using global::squad.Transcripts;
using global::squad.Ui.Abstractions;

namespace squad.Application;

public sealed class AgentRoleState
{
    private readonly object myStateLock = new();
    private readonly RoleTranscriptState myTranscript;

    internal AgentRoleState(
        string role,
        TranscriptArchive transcriptArchive,
        TranscriptRetentionOptions retentionOptions)
    {
        Role = role;
        myTranscript = new RoleTranscriptState(role, transcriptArchive, retentionOptions, myStateLock);
    }

    public string Role { get; }
    public string Status { get; internal set; } = "starting";
    public DateTimeOffset? LastEventAt { get; internal set; }
    public string? Error { get; internal set; }
    public string? ActiveTool { get; internal set; }
    public bool IsWorking { get; internal set; }
    public string? Model { get; internal set; }
    public string? Effort { get; internal set; }
    public decimal? AicUsed { get; internal set; }
    public long? ContextUsedTokens { get; internal set; }
    public long? ContextLimitTokens { get; internal set; }
    public int EventCount { get; internal set; }
    public IReadOnlyList<TranscriptEntry> TranscriptEntries => myTranscript.Entries;

    internal object SyncRoot => myStateLock;
    internal RoleTranscriptState Transcript => myTranscript;

    internal AgentRoleSnapshot CreateSnapshot()
    {
        lock (myStateLock)
        {
            return new AgentRoleSnapshot(
                Role,
                Status,
                LastEventAt,
                Error,
                ActiveTool,
                IsWorking,
                Model,
                Effort,
                AicUsed,
                ContextUsedTokens,
                ContextLimitTokens,
                EventCount);
        }
    }
}
