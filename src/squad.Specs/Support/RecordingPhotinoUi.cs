using System.Collections.Concurrent;
using System.Text.Json;
using global::squad.AgentProvider.Abstractions.Agents;
using global::squad.Ui.Abstractions;

namespace squad.Specs.Support;

public sealed class RecordingPhotinoUi : ISquadUi, ITranscriptUi
{
    private static readonly DateTimeOffset myOccurredAt =
        DateTimeOffset.Parse("2026-09-03T00:00:00Z");
    private readonly ConcurrentQueue<string> myCalls = new();
    private readonly TaskCompletionSource myUrlCompletionStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myReleaseUrlCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, AgentElicitationRequest>
        myElicitations = new(StringComparer.Ordinal)
        {
            ["form-1"] = new(
                myOccurredAt,
                "form-1",
                "writer",
                "Provide details.",
                "form"),
            ["url-1"] = new(
                myOccurredAt,
                "url-1",
                "writer",
                "Authorize.",
                "url",
                Url: "https://example.test/authorize"),
        };

    public string? PromptFailure { get; set; }
    public bool BlockUrlCompletion { get; set; }
    public IReadOnlyList<string> Calls => myCalls.ToArray();
    public Task UrlCompletionStarted => myUrlCompletionStarted.Task;

    public event Action<UiRefreshPriority>? SnapshotRequested
    {
        add { }
        remove { }
    }

    public event Action<TranscriptUpdate>? TranscriptChanged
    {
        add { }
        remove { }
    }

    public void RecordOpenedUrl(string url) =>
        myCalls.Enqueue($"url.open:{url}");

    public void ReleaseUrlCompletion() =>
        myReleaseUrlCompletion.TrySetResult();

    public void ClearCalls() => myCalls.Clear();

    public JsonElement CreateSnapshot()
    {
        myCalls.Enqueue("snapshot");
        return JsonSerializer.SerializeToElement(new { state = "recording" });
    }

    public AgentElicitationRequest GetPendingElicitation(
        string role,
        string requestId)
    {
        myCalls.Enqueue($"elicitation.lookup:{role}:{requestId}");
        if (!myElicitations.TryGetValue(requestId, out var request)
            || request.Role != role)
            throw new InvalidOperationException(
                $"Unknown elicitation '{requestId}' for role '{role}'.");
        return request;
    }

    public Task SendAsync(
        string role,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        myCalls.Enqueue($"prompt:{role}:{prompt}");
        return PromptFailure is null
            ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException(PromptFailure));
    }

    public Task AbortAsync(
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        myCalls.Enqueue($"abort:{role}");
        return Task.CompletedTask;
    }

    public Task CompletePermissionAsync(
        string role,
        string requestId,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        myCalls.Enqueue(
            $"permission:{role}:{requestId}:{approved.ToString().ToLowerInvariant()}");
        return Task.CompletedTask;
    }

    public Task CompleteInputAsync(
        string role,
        string requestId,
        string? answer,
        bool wasFreeform,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        myCalls.Enqueue(
            $"input:{role}:{requestId}:{answer ?? "null"}:"
            + wasFreeform.ToString().ToLowerInvariant());
        return Task.CompletedTask;
    }

    public async Task CompleteElicitationAsync(
        string role,
        string requestId,
        string action,
        JsonElement? content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var call = $"{role}:{requestId}:{action}:"
            + (content?.GetRawText() ?? "null");
        if (BlockUrlCompletion && requestId == "url-1")
        {
            myCalls.Enqueue($"elicitation.complete.started:{call}");
            myUrlCompletionStarted.TrySetResult();
            await myReleaseUrlCompletion.Task.WaitAsync(cancellationToken);
        }
        myCalls.Enqueue($"elicitation.complete:{call}");
    }

    public IReadOnlyList<RoleTranscriptSnapshot> CreateTranscriptSnapshot(
        int maxEntriesPerRole)
    {
        myCalls.Enqueue($"transcript.snapshot:{maxEntriesPerRole}");
        return
        [
            new(
                "coder",
                7,
                [new(4, new(myOccurredAt, "assistant", "snapshot entry"))],
                HasMore: true,
                HistoryTruncated: false),
        ];
    }

    public RoleTranscriptPage CreateTranscriptPage(
        string role,
        int beforeIndex,
        int maxEntries)
    {
        myCalls.Enqueue(
            $"transcript.page:{role}:{beforeIndex}:{maxEntries}");
        return new(
            role,
            [new(beforeIndex - 1, new(myOccurredAt, "assistant", "page entry"))],
            HasMore: false,
            HistoryTruncated: false);
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(
        string role,
        int entryIndex)
    {
        myCalls.Enqueue($"transcript.entry:{role}:{entryIndex}");
        return new(
            role,
            Sequence: 8,
            entryIndex,
            new(myOccurredAt, "assistant", "archived entry"),
            ContentTruncated: false,
            TotalContentCharacters: 14,
            ArchivedPrefixCharacters: 0);
    }
}




