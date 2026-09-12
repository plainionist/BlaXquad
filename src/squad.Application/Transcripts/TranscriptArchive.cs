using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Application.Transcripts;

/// <summary>
/// Maintains a private, size-bounded on-disk transcript archive that can outlive entries evicted from live role
/// state. Disposing the archive removes its temporary directory and all retained history.
/// </summary>
internal sealed class TranscriptArchive : IDisposable
{
    private const string myTruncationMarker = "\n[Transcript content truncated at the configured storage limit.]";
    private const UnixFileMode myPrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode myPrivateFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly UTF8Encoding myUtf8NoBom = new(false);
    private readonly string myDirectory;
    private readonly TranscriptRetentionOptions myOptions;
    private readonly object myStateLock = new();
    private readonly Dictionary<SquadMemberId, SortedDictionary<int, int>> myEntryLengths = [];
    private readonly Dictionary<(SquadMemberId MemberId, int EntryIndex), long> myTotalEntryLengths = [];
    private readonly HashSet<(SquadMemberId MemberId, int EntryIndex)> myContentTruncatedEntries = [];
    private readonly HashSet<SquadMemberId> myTruncatedRoles = [];
    private bool myDisposed;

    public TranscriptArchive(TranscriptRetentionOptions options)
    {
        myOptions = options;
        myDirectory = Path.Combine(
            Path.GetTempPath(),
            $"blaxquad-transcript-history-{Guid.NewGuid():N}");
    }

    internal void Apply(TranscriptUpdate update)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            var entries = GetEntries(update.MemberId);
            var path = GetContentPath(update.MemberId, update.EntryIndex);
            switch (update.Kind)
            {
                case TranscriptUpdateKind.AppendEntry:
                    WriteEntry(update.MemberId, update.EntryIndex, update.Entry!);
                    entries[update.EntryIndex] = Math.Min(
                        update.Entry!.Content.Length,
                        myOptions.MaxArchivedEntryCharacters);
                    myTotalEntryLengths[(update.MemberId, update.EntryIndex)] =
                        update.Entry.Content.Length;
                    RecordContentTruncation(update.MemberId, update.EntryIndex, update.Entry.Content);
                    break;
                case TranscriptUpdateKind.AppendContent:
                    AppendContent(update.MemberId, path, entries, update.EntryIndex, update.Content!);
                    break;
                case TranscriptUpdateKind.ReplaceEntry:
                    WriteEntry(update.MemberId, update.EntryIndex, update.Entry!);
                    entries[update.EntryIndex] = Math.Min(
                        update.Entry!.Content.Length,
                        myOptions.MaxArchivedEntryCharacters);
                    myTotalEntryLengths[(update.MemberId, update.EntryIndex)] =
                        update.Entry.Content.Length;
                    RecordContentTruncation(update.MemberId, update.EntryIndex, update.Entry.Content);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(update.Kind));
            }
            EnforceLimits(update.MemberId, entries);
        }
    }

    internal IReadOnlyList<IndexedTranscriptEntry> ReadPage(
        SquadMemberId memberId,
        int beforeIndex,
        int maxEntries)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            if (!myEntryLengths.TryGetValue(memberId, out var entries))
            {
                return [];
            }
            return entries.Keys
                .Where(index => index < beforeIndex)
                .TakeLast(maxEntries)
                .Select(index => new IndexedTranscriptEntry(index, ReadEntryCore(memberId, index)))
                .ToArray();
        }
    }

    internal bool HasEntriesBefore(SquadMemberId memberId, int beforeIndex) =>
        WithEntries(memberId, entries => entries.Keys.Any(index => index < beforeIndex));

    internal bool HasMoreContent(SquadMemberId memberId, int entryIndex, long retainedContentStart) =>
        WithEntries(memberId, entries =>
            retainedContentStart > 0
            && entries.ContainsKey(entryIndex)
            && (!myContentTruncatedEntries.Contains((memberId, entryIndex))
                || myOptions.MaxArchivedEntryCharacters > myTruncationMarker.Length));

    internal RoleArchivedTranscriptEntry ReadEntry(SquadMemberId memberId, int entryIndex, long sequence)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            if (!myEntryLengths.TryGetValue(memberId, out var entries)
                || !entries.ContainsKey(entryIndex))
            {
                return new RoleArchivedTranscriptEntry(
                    memberId,
                    sequence,
                    entryIndex,
                    null,
                    false,
                    0,
                    0);
            }
            var contentTruncated = myContentTruncatedEntries.Contains((memberId, entryIndex));
            return new RoleArchivedTranscriptEntry(
                memberId,
                sequence,
                entryIndex,
                ReadEntryCore(memberId, entryIndex),
                contentTruncated,
                myTotalEntryLengths[(memberId, entryIndex)],
                contentTruncated
                    ? Math.Max(0, myOptions.MaxArchivedEntryCharacters - myTruncationMarker.Length)
                    : entries[entryIndex]);
        }
    }

    internal bool HasEntriesOutside(SquadMemberId memberId, IReadOnlyCollection<int> includedIndices) =>
        WithEntries(memberId, entries =>
        {
            var included = includedIndices.ToHashSet();
            return entries.Keys.Any(index => !included.Contains(index));
        });

    internal bool WasTruncated(SquadMemberId memberId)
    {
        lock (myStateLock)
            return myTruncatedRoles.Contains(memberId);
    }

    public void Dispose()
    {
        lock (myStateLock)
        {
            if (myDisposed)
            {
                return;
            }
            myDisposed = true;
            if (Directory.Exists(myDirectory))
            {
                Directory.Delete(myDirectory, recursive: true);
            }
        }
    }

    private SortedDictionary<int, int> GetEntries(SquadMemberId memberId)
    {
        if (!myEntryLengths.TryGetValue(memberId, out var entries))
        {
            myEntryLengths[memberId] = entries = [];
        }
        return entries;
    }

    private void WriteEntry(SquadMemberId memberId, int entryIndex, TranscriptEntry entry)
    {
        var directory = GetRoleDirectory(memberId);
        CreatePrivateDirectory(myDirectory);
        CreatePrivateDirectory(directory);
        WritePrivateText(
            GetMetadataPath(memberId, entryIndex),
            JsonSerializer.Serialize(new { entry.OccurredAt, entry.Source }),
            append: false);
        WritePrivateText(
            GetContentPath(memberId, entryIndex),
            LimitContent(entry.Content, myOptions.MaxArchivedEntryCharacters),
            append: false);
    }

    private void AppendContent(
        SquadMemberId memberId,
        string path,
        SortedDictionary<int, int> entries,
        int entryIndex,
        string content)
    {
        if (!entries.TryGetValue(entryIndex, out var currentLength))
        {
            return;
        }
        myTotalEntryLengths[(memberId, entryIndex)] =
            myTotalEntryLengths.GetValueOrDefault((memberId, entryIndex)) + content.Length;
        if (myContentTruncatedEntries.Contains((memberId, entryIndex)))
        {
            return;
        }
        var maxCharacters = myOptions.MaxArchivedEntryCharacters;
        if (content.Length <= maxCharacters - currentLength)
        {
            WritePrivateText(path, content, append: true);
            entries[entryIndex] = currentLength + content.Length;
            return;
        }

        var retained = File.ReadAllText(path);
        var contentLimit = Math.Max(0, maxCharacters - myTruncationMarker.Length);
        if (retained.Length < contentLimit)
        {
            retained += content[..Math.Min(content.Length, contentLimit - retained.Length)];
        }
        var marker = myTruncationMarker[..Math.Min(myTruncationMarker.Length, maxCharacters)];
        WritePrivateText(path, retained[..Math.Min(retained.Length, contentLimit)] + marker, append: false);
        entries[entryIndex] = maxCharacters;
        myContentTruncatedEntries.Add((memberId, entryIndex));
        myTruncatedRoles.Add(memberId);
    }

    private TranscriptEntry ReadEntryCore(SquadMemberId memberId, int entryIndex)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(GetMetadataPath(memberId, entryIndex)));
        return new TranscriptEntry(
            metadata.RootElement.GetProperty("OccurredAt").GetDateTimeOffset(),
            metadata.RootElement.GetProperty("Source").GetString()!,
            File.ReadAllText(GetContentPath(memberId, entryIndex)));
    }

    private void EnforceLimits(SquadMemberId memberId, SortedDictionary<int, int> entries)
    {
        var totalCharacters = entries.Values.Sum();
        while (entries.Count > myOptions.MaxArchivedEntries
            || totalCharacters > myOptions.MaxArchivedContentCharacters)
        {
            var oldest = entries.First();
            File.Delete(GetMetadataPath(memberId, oldest.Key));
            File.Delete(GetContentPath(memberId, oldest.Key));
            entries.Remove(oldest.Key);
            myContentTruncatedEntries.Remove((memberId, oldest.Key));
            myTotalEntryLengths.Remove((memberId, oldest.Key));
            totalCharacters -= oldest.Value;
            myTruncatedRoles.Add(memberId);
        }
    }

    private string GetRoleDirectory(SquadMemberId memberId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(memberId.Value)));
        return Path.Combine(myDirectory, hash);
    }

    private string GetMetadataPath(SquadMemberId memberId, int entryIndex) =>
        Path.Combine(GetRoleDirectory(memberId), $"{entryIndex:D12}.json");

    private string GetContentPath(SquadMemberId memberId, int entryIndex) =>
        Path.Combine(GetRoleDirectory(memberId), $"{entryIndex:D12}.txt");

    private bool WithEntries(
        SquadMemberId memberId,
        Func<SortedDictionary<int, int>, bool> predicate)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            return myEntryLengths.TryGetValue(memberId, out var entries)
                && predicate(entries);
        }
    }

    private static string LimitContent(string content, int maxCharacters)
    {
        if (content.Length <= maxCharacters)
        {
            return content;
        }
        if (maxCharacters <= myTruncationMarker.Length)
        {
            return myTruncationMarker[..maxCharacters];
        }
        var contentLength = Math.Max(0, maxCharacters - myTruncationMarker.Length);
        return content[..contentLength] + myTruncationMarker;
    }

    private void RecordContentTruncation(SquadMemberId memberId, int entryIndex, string content)
    {
        if (content.Length > myOptions.MaxArchivedEntryCharacters)
        {
            myContentTruncatedEntries.Add((memberId, entryIndex));
            myTruncatedRoles.Add(memberId);
        }
        else
        {
            myContentTruncatedEntries.Remove((memberId, entryIndex));
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, myPrivateDirectoryMode);
        File.SetUnixFileMode(path, myPrivateDirectoryMode);
    }

    private static void WritePrivateText(string path, string content, bool append)
    {
        if (OperatingSystem.IsWindows())
        {
            if (append)
            {
                File.AppendAllText(path, content);
            }
            else
            {
                File.WriteAllText(path, content);
            }
            return;
        }

        using var stream = new FileStream(path, new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = append ? FileMode.Append : FileMode.Create,
            Share = FileShare.Read,
            UnixCreateMode = myPrivateFileMode,
        });
        using var writer = new StreamWriter(stream, myUtf8NoBom);
        writer.Write(content);
    }
}
