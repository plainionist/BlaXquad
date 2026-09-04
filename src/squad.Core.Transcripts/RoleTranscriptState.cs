using global::squad.Ui.Abstractions;

namespace squad.Core.Transcripts;

/// <summary>
/// Owns the retained transcript, streaming buffers, tool-call correlation, archive access, and retention policy for
/// one role. Read methods (snapshot/page/archive/entries) lock the shared role synchronization root themselves;
/// mutation methods rely on the caller (the event projector) already holding that same lock so status and transcript
/// changes commit atomically.
/// </summary>
public sealed class RoleTranscriptState
{
    private const string myArchivedContentAvailableMarker =
        "[Earlier content is available in transcript history.]\n";
    private const string myArchivedContentUnavailableMarker =
        "[Earlier content is no longer available.]\n";
    private readonly string myRole;
    private readonly object mySyncRoot;
    private readonly List<IndexedTranscriptEntry> myTranscriptEntries = [];
    private readonly HashSet<int> myProtectedTranscriptEntries = [];
    private readonly Dictionary<string, ToolTranscriptState> myToolTranscriptEntries = new(StringComparer.Ordinal);
    private readonly TranscriptArchive myTranscriptArchive;
    private readonly TranscriptRetentionOptions myRetentionOptions;
    private TranscriptEntryBuffer? myAssistantEntryBuffer;
    private TranscriptEntryBuffer? myReasoningEntryBuffer;
    private int? myAssistantTranscriptEntryIndex;
    private int? myReasoningTranscriptEntryIndex;
    private long myTranscriptSequence;
    private int myNextTranscriptEntryIndex;
    private int myRetainedContentCharacters;

    public RoleTranscriptState(
        string role,
        TranscriptArchive transcriptArchive,
        TranscriptRetentionOptions retentionOptions,
        object syncRoot)
    {
        myRole = role;
        myTranscriptArchive = transcriptArchive;
        myRetentionOptions = retentionOptions;
        mySyncRoot = syncRoot;
    }

    public IReadOnlyList<TranscriptEntry> Entries
    {
        get
        {
            lock (mySyncRoot)
                return CreateTranscriptEntries().Select(entry => entry.Entry).ToArray();
        }
    }

    public RoleTranscriptSnapshot CreateTranscriptSnapshot(int maxEntries)
    {
        lock (mySyncRoot)
        {
            var endIndex = myTranscriptEntries.Count;
            var startIndex = Math.Max(0, endIndex - maxEntries);
            var entries = CreateTranscriptEntries()
                .Skip(startIndex)
                .ToArray();
            return new RoleTranscriptSnapshot(
                myRole,
                myTranscriptSequence,
                entries,
                myTranscriptArchive.HasEntriesOutside(
                    myRole,
                    entries.Select(entry => entry.EntryIndex).ToArray()),
                myTranscriptArchive.WasTruncated(myRole));
        }
    }

    public RoleTranscriptPage CreateTranscriptPage(int beforeIndex, int maxEntries)
    {
        lock (mySyncRoot)
        {
            var entries = myTranscriptArchive.ReadPage(myRole, beforeIndex, maxEntries);
            var firstIndex = entries.FirstOrDefault()?.EntryIndex ?? beforeIndex;
            return new RoleTranscriptPage(
                myRole,
                entries,
                myTranscriptArchive.HasEntriesBefore(myRole, firstIndex),
                myTranscriptArchive.WasTruncated(myRole));
        }
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(int entryIndex)
    {
        lock (mySyncRoot)
            return myTranscriptArchive.ReadEntry(myRole, entryIndex, myTranscriptSequence);
    }

    public TranscriptUpdate AddTranscriptEntry(TranscriptEntry entry, bool protect = false)
    {
        var entryIndex = myNextTranscriptEntryIndex++;
        var update = CreateUpdate(
            TranscriptUpdateKind.AppendEntry,
            entryIndex,
            entry,
            null,
            CreateAnnouncement(
                entryIndex,
                TranscriptAnnouncementKind.AppendEntry,
                entry.Content));
        myTranscriptArchive.Apply(update);
        var (retainedContent, contentStart) = LimitRetainedContent(entry.Content);
        var retainedEntry = entry with { Content = retainedContent };
        myTranscriptEntries.Add(new IndexedTranscriptEntry(
            entryIndex,
            retainedEntry,
            ContentStart: contentStart));
        if (protect)
            myProtectedTranscriptEntries.Add(entryIndex);
        myRetainedContentCharacters += retainedEntry.Content.Length;
        EnforceRetentionLimits();
        return update with
        {
            Entry = retainedEntry,
            HasArchivedContent = myTranscriptArchive.HasMoreContent(
                myRole,
                entryIndex,
                contentStart),
            ContentStart = contentStart,
        };
    }

    public TranscriptUpdate StartTool(
        string toolCallId,
        string toolName,
        bool suppressOutput,
        bool appendLineCount,
        TranscriptEntry entry)
    {
        var update = AddTranscriptEntry(entry, protect: true);
        myToolTranscriptEntries[toolCallId] = new ToolTranscriptState(
            update.EntryIndex,
            toolName,
            entry,
            "",
            null,
            suppressOutput,
            appendLineCount);
        return update;
    }

    public TranscriptUpdate? ChangeToolOutput(
        string toolCallId,
        string output)
    {
        if (!myToolTranscriptEntries.TryGetValue(toolCallId, out var tool))
            return null;
        tool = tool with { Output = output, Progress = null };
        myToolTranscriptEntries[toolCallId] = tool;
        if (tool.SuppressOutput)
            return null;
        return ReplaceTranscriptEntry(tool.EntryIndex, CreateToolEntry(tool));
    }

    public TranscriptUpdate? ChangeToolProgress(
        string toolCallId,
        string progress)
    {
        if (!myToolTranscriptEntries.TryGetValue(toolCallId, out var tool))
            return null;
        if (tool.SuppressOutput)
            return null;
        tool = tool with { Progress = progress };
        myToolTranscriptEntries[toolCallId] = tool;
        return ReplaceTranscriptEntry(tool.EntryIndex, CreateToolEntry(tool));
    }

    public ToolCompletionResult? CompleteTool(
        string toolCallId,
        string? displayOutputFallback,
        string? contentFallback)
    {
        if (!myToolTranscriptEntries.Remove(toolCallId, out var tool))
            return null;
        TranscriptUpdate? update = null;
        if (tool.SuppressOutput)
        {
            var content = tool.Output.Length > 0 ? tool.Output : contentFallback;
            if (tool.AppendLineCount && !string.IsNullOrEmpty(content))
            {
                var lineCount = CountLines(content);
                tool = tool with { Entry = tool.Entry with { Content = $"{tool.Entry.Content} [1..{lineCount}]" } };
                update = ReplaceTranscriptEntry(tool.EntryIndex, tool.Entry);
            }
        }
        else if (tool.Output.Length == 0 && !string.IsNullOrEmpty(displayOutputFallback))
        {
            tool = tool with { Output = displayOutputFallback, Progress = null };
            update = ReplaceTranscriptEntry(tool.EntryIndex, CreateToolEntry(tool));
        }
        else if (tool.Progress is not null)
        {
            tool = tool with { Progress = null };
            update = ReplaceTranscriptEntry(tool.EntryIndex, CreateToolEntry(tool));
        }
        myProtectedTranscriptEntries.Remove(tool.EntryIndex);
        var activeTool = myToolTranscriptEntries.Values.LastOrDefault()?.ToolName;
        EnforceRetentionLimits();
        return new ToolCompletionResult(update, activeTool);
    }

    private static int CountLines(string content) =>
        content.Length == 0
            ? 0
            : content.Count(character => character == '\n') + (content[^1] == '\n' ? 0 : 1);

    public TranscriptUpdate AppendAssistantEntry(DateTimeOffset occurredAt, string content) =>
        AppendStreamingEntry(
            ref myAssistantEntryBuffer,
            ref myAssistantTranscriptEntryIndex,
            occurredAt,
            "assistant",
            content);

    public TranscriptUpdate AppendReasoningEntry(DateTimeOffset occurredAt, string content) =>
        AppendStreamingEntry(
            ref myReasoningEntryBuffer,
            ref myReasoningTranscriptEntryIndex,
            occurredAt,
            "reasoning",
            content);

    public TranscriptUpdate CompleteAssistantEntry(DateTimeOffset occurredAt, string content) =>
        CompleteStreamingEntry(
            ref myAssistantEntryBuffer,
            ref myAssistantTranscriptEntryIndex,
            new TranscriptEntry(occurredAt, "assistant", content));

    public TranscriptUpdate CompleteReasoningEntry(DateTimeOffset occurredAt, string content) =>
        CompleteStreamingEntry(
            ref myReasoningEntryBuffer,
            ref myReasoningTranscriptEntryIndex,
            new TranscriptEntry(occurredAt, "reasoning", content));

    public void FinalizeAssistantEntry() =>
        FinalizeStreamingEntry(ref myAssistantEntryBuffer, ref myAssistantTranscriptEntryIndex);

    public void FinalizeReasoningEntry() =>
        FinalizeStreamingEntry(ref myReasoningEntryBuffer, ref myReasoningTranscriptEntryIndex);

    public void UnprotectTranscriptEntry(int entryIndex)
    {
        myProtectedTranscriptEntries.Remove(entryIndex);
        EnforceRetentionLimits();
    }

    private TranscriptUpdate AppendStreamingEntry(
        ref TranscriptEntryBuffer? buffer,
        ref int? entryIndex,
        DateTimeOffset occurredAt,
        string source,
        string content)
    {
        if (buffer is null)
        {
            buffer = new TranscriptEntryBuffer(
                occurredAt,
                source,
                Math.Min(
                    myRetentionOptions.MaxRetainedEntryCharacters,
                    myRetentionOptions.MaxRetainedContentCharacters / 2));
            entryIndex = myNextTranscriptEntryIndex++;
            myTranscriptEntries.Add(new IndexedTranscriptEntry(
                entryIndex.Value,
                new TranscriptEntry(occurredAt, source, "")));
            buffer.Append(content);
            var update = CreateUpdate(
                TranscriptUpdateKind.AppendEntry,
                entryIndex.Value,
                new TranscriptEntry(occurredAt, source, content),
                null,
                CreateAnnouncement(
                    entryIndex.Value,
                    TranscriptAnnouncementKind.AppendEntry,
                    content));
            myTranscriptArchive.Apply(update);
            EnforceRetentionLimits();
            var retainedEntry = buffer.CreateEntry();
            return update with
            {
                Entry = retainedEntry,
                HasArchivedContent = myTranscriptArchive.HasMoreContent(
                    myRole,
                    entryIndex.Value,
                    buffer.ContentStart),
                ContentStart = buffer.ContentStart,
            };
        }
        buffer.Append(content);
        myTranscriptArchive.Apply(new TranscriptUpdate(
            myRole,
            0,
            TranscriptUpdateKind.AppendContent,
            entryIndex!.Value,
            null,
            content));
        EnforceRetentionLimits();
        if (!buffer.IsTruncated)
            return CreateUpdate(
                TranscriptUpdateKind.AppendContent,
                entryIndex.Value,
                null,
                content,
                CreateAnnouncement(
                    entryIndex.Value,
                    TranscriptAnnouncementKind.AppendContent,
                    content));
        var materializedEntry = buffer.CreateEntry();
        return CreateUpdate(
            TranscriptUpdateKind.ReplaceEntry,
            entryIndex.Value,
            materializedEntry,
            null,
            CreateAnnouncement(
                entryIndex.Value,
                TranscriptAnnouncementKind.AppendContent,
                content)) with
        {
            HasArchivedContent = myTranscriptArchive.HasMoreContent(
                myRole,
                entryIndex.Value,
                buffer.ContentStart),
            ContentStart = buffer.ContentStart,
        };
    }

    private TranscriptUpdate CompleteStreamingEntry(
        ref TranscriptEntryBuffer? buffer,
        ref int? entryIndex,
        TranscriptEntry entry)
    {
        if (entryIndex is int index)
        {
            var announcement = buffer?.Matches(entry.Content) == true
                ? null
                : CreateAnnouncement(
                    index,
                    TranscriptAnnouncementKind.Replace,
                    entry.Content);
            var (retainedContent, contentStart) = LimitRetainedContent(entry.Content);
            var retainedEntry = entry with { Content = retainedContent };
            var localIndex = myTranscriptEntries.FindIndex(item => item.EntryIndex == index);
            if (localIndex >= 0)
            {
                myRetainedContentCharacters -= myTranscriptEntries[localIndex].Entry.Content.Length;
                myTranscriptEntries[localIndex] = new IndexedTranscriptEntry(
                    index,
                    retainedEntry,
                    ContentStart: contentStart);
                myRetainedContentCharacters += retainedEntry.Content.Length;
            }
            buffer = null;
            entryIndex = null;
            var update = CreateUpdate(
                TranscriptUpdateKind.ReplaceEntry,
                index,
                entry,
                null,
                announcement);
            myTranscriptArchive.Apply(update);
            EnforceRetentionLimits();
            return update with
            {
                Entry = retainedEntry,
                HasArchivedContent = myTranscriptArchive.HasMoreContent(
                    myRole,
                    index,
                    contentStart),
                ContentStart = contentStart,
            };
        }
        buffer = null;
        entryIndex = null;
        return AddTranscriptEntry(entry);
    }

    private TranscriptUpdate ReplaceTranscriptEntry(
        int entryIndex,
        TranscriptEntry entry)
    {
        var (retainedContent, contentStart) = LimitRetainedContent(entry.Content);
        var retainedEntry = entry with { Content = retainedContent };
        var localIndex = myTranscriptEntries.FindIndex(item => item.EntryIndex == entryIndex);
        if (localIndex >= 0)
        {
            myRetainedContentCharacters -= myTranscriptEntries[localIndex].Entry.Content.Length;
            myTranscriptEntries[localIndex] = new IndexedTranscriptEntry(
                entryIndex,
                retainedEntry,
                ContentStart: contentStart);
            myRetainedContentCharacters += retainedEntry.Content.Length;
        }
        var update = CreateUpdate(
            TranscriptUpdateKind.ReplaceEntry,
            entryIndex,
            entry,
            null,
            CreateAnnouncement(
                entryIndex,
                TranscriptAnnouncementKind.Replace,
                entry.Content));
        myTranscriptArchive.Apply(update);
        EnforceRetentionLimits();
        return update with
        {
            Entry = retainedEntry,
            HasArchivedContent = myTranscriptArchive.HasMoreContent(
                myRole,
                entryIndex,
                contentStart),
            ContentStart = contentStart,
        };
    }

    private static TranscriptEntry CreateToolEntry(ToolTranscriptState tool)
    {
        var detail = tool.Progress ?? tool.Output;
        return detail.Length == 0
            ? tool.Entry
            : tool.Entry with { Content = $"{tool.Entry.Content}\n{detail}" };
    }

    private void FinalizeStreamingEntry(
        ref TranscriptEntryBuffer? buffer,
        ref int? entryIndex)
    {
        if (buffer is not null && entryIndex is int index)
        {
            var localIndex = myTranscriptEntries.FindIndex(item => item.EntryIndex == index);
            if (localIndex >= 0)
            {
                var entry = buffer.CreateEntry();
                myTranscriptEntries[localIndex] = new IndexedTranscriptEntry(
                    index,
                    entry,
                    ContentStart: buffer.ContentStart);
                myRetainedContentCharacters += entry.Content.Length;
            }
        }
        buffer = null;
        entryIndex = null;
        EnforceRetentionLimits();
    }

    private IndexedTranscriptEntry[] CreateTranscriptEntries()
    {
        var entries = myTranscriptEntries.ToArray();
        MaterializeStreamingEntry(entries, myAssistantEntryBuffer, myAssistantTranscriptEntryIndex);
        MaterializeStreamingEntry(entries, myReasoningEntryBuffer, myReasoningTranscriptEntryIndex);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var hasArchivedContent = myTranscriptArchive.HasMoreContent(
                myRole,
                entry.EntryIndex,
                entry.ContentStart);
            entries[index] = entry with
            {
                Entry = hasArchivedContent || entry.ContentStart == 0
                    ? entry.Entry
                    : entry.Entry with
                    {
                        Content = MarkArchivedContentUnavailable(entry.Entry.Content),
                    },
                HasArchivedContent = hasArchivedContent,
            };
        }
        return entries;
    }

    private TranscriptUpdate CreateUpdate(
        TranscriptUpdateKind kind,
        int entryIndex,
        TranscriptEntry? entry,
        string? content,
        TranscriptAnnouncement? announcement) =>
        new(
            myRole,
            ++myTranscriptSequence,
            kind,
            entryIndex,
            entry,
            content,
            Announcement: announcement);

    private sealed record ToolTranscriptState(
        int EntryIndex,
        string ToolName,
        TranscriptEntry Entry,
        string Output,
        string? Progress,
        bool SuppressOutput,
        bool AppendLineCount);

    private TranscriptAnnouncement CreateAnnouncement(
        int entryIndex,
        TranscriptAnnouncementKind kind,
        string content)
    {
        var maximumCharacters = myRetentionOptions.MaxAnnouncementCharacters;
        if (content.Length <= maximumCharacters)
            return new(entryIndex, kind, content);
        return new(
            entryIndex,
            kind,
            content[^maximumCharacters..],
            Truncated: true);
    }

    private static void MaterializeStreamingEntry(
        IndexedTranscriptEntry[] entries,
        TranscriptEntryBuffer? buffer,
        int? entryIndex)
    {
        if (buffer is null || entryIndex is not int index)
            return;
        var localIndex = Array.FindIndex(entries, item => item.EntryIndex == index);
        if (localIndex >= 0)
            entries[localIndex] = new IndexedTranscriptEntry(
                index,
                buffer.CreateEntry(),
                ContentStart: buffer.ContentStart);
    }

    private void EnforceRetentionLimits()
    {
        while (myTranscriptEntries.Count > myRetentionOptions.MaxRetainedEntries
            || RetainedContentCharacters() > myRetentionOptions.MaxRetainedContentCharacters)
        {
            var removableIndex = myTranscriptEntries.FindIndex(item =>
                item.EntryIndex != myAssistantTranscriptEntryIndex
                && item.EntryIndex != myReasoningTranscriptEntryIndex
                && !myProtectedTranscriptEntries.Contains(item.EntryIndex));
            if (removableIndex < 0)
                removableIndex = myTranscriptEntries.FindIndex(item =>
                    item.EntryIndex != myAssistantTranscriptEntryIndex
                    && item.EntryIndex != myReasoningTranscriptEntryIndex);
            if (removableIndex < 0)
                return;
            myRetainedContentCharacters -= myTranscriptEntries[removableIndex].Entry.Content.Length;
            myTranscriptEntries.RemoveAt(removableIndex);
        }
    }

    private int RetainedContentCharacters() =>
        myRetainedContentCharacters
        + (myAssistantEntryBuffer?.Length ?? 0)
        + (myReasoningEntryBuffer?.Length ?? 0);

    private (string Content, long ContentStart) LimitRetainedContent(string content)
    {
        var maxCharacters = myRetentionOptions.MaxRetainedEntryCharacters;
        if (content.Length <= maxCharacters)
            return (content, 0);
        var contentLength = Math.Max(0, maxCharacters - myArchivedContentAvailableMarker.Length);
        var retainedMarker = myArchivedContentAvailableMarker[
            ..Math.Min(myArchivedContentAvailableMarker.Length, maxCharacters)];
        return (retainedMarker + content[^contentLength..], content.Length - contentLength);
    }

    private static string MarkArchivedContentUnavailable(string content)
    {
        if (content.StartsWith(myArchivedContentAvailableMarker, StringComparison.Ordinal))
            return myArchivedContentUnavailableMarker
                + content[myArchivedContentAvailableMarker.Length..];
        return myArchivedContentUnavailableMarker[
            ..Math.Min(myArchivedContentUnavailableMarker.Length, content.Length)];
    }
}
