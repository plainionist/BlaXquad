using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using global::squad.Ui.Abstractions;

namespace squad.Core.Transcripts;

public sealed class TranscriptArchive : IDisposable
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
    private readonly Dictionary<string, SortedDictionary<int, int>> myEntryLengths = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Role, int EntryIndex), long> myTotalEntryLengths = [];
    private readonly HashSet<(string Role, int EntryIndex)> myContentTruncatedEntries = [];
    private readonly HashSet<string> myTruncatedRoles = new(StringComparer.Ordinal);
    private bool myDisposed;

    public TranscriptArchive(TranscriptRetentionOptions options)
    {
        myOptions = options;
        myDirectory = Path.Combine(
            Path.GetTempPath(),
            $"blaxquad-transcript-history-{Guid.NewGuid():N}");
    }

    public string DirectoryPath => myDirectory;

    internal void Apply(TranscriptUpdate update)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            var entries = GetEntries(update.Role);
            var path = GetContentPath(update.Role, update.EntryIndex);
            switch (update.Kind)
            {
                case TranscriptUpdateKind.AppendEntry:
                    WriteEntry(update.Role, update.EntryIndex, update.Entry!);
                    entries[update.EntryIndex] = Math.Min(
                        update.Entry!.Content.Length,
                        myOptions.MaxArchivedEntryCharacters);
                    myTotalEntryLengths[(update.Role, update.EntryIndex)] =
                        update.Entry.Content.Length;
                    RecordContentTruncation(update.Role, update.EntryIndex, update.Entry.Content);
                    break;
                case TranscriptUpdateKind.AppendContent:
                    AppendContent(update.Role, path, entries, update.EntryIndex, update.Content!);
                    break;
                case TranscriptUpdateKind.ReplaceEntry:
                    WriteEntry(update.Role, update.EntryIndex, update.Entry!);
                    entries[update.EntryIndex] = Math.Min(
                        update.Entry!.Content.Length,
                        myOptions.MaxArchivedEntryCharacters);
                    myTotalEntryLengths[(update.Role, update.EntryIndex)] =
                        update.Entry.Content.Length;
                    RecordContentTruncation(update.Role, update.EntryIndex, update.Entry.Content);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(update.Kind));
            }
            EnforceLimits(update.Role, entries);
        }
    }

    internal IReadOnlyList<IndexedTranscriptEntry> ReadPage(
        string role,
        int beforeIndex,
        int maxEntries)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            if (!myEntryLengths.TryGetValue(role, out var entries))
                return [];
            return entries.Keys
                .Where(index => index < beforeIndex)
                .TakeLast(maxEntries)
                .Select(index => new IndexedTranscriptEntry(index, ReadEntryCore(role, index)))
                .ToArray();
        }
    }

    internal bool HasEntriesBefore(string role, int beforeIndex) =>
        WithEntries(role, entries => entries.Keys.Any(index => index < beforeIndex));

    internal bool HasMoreContent(string role, int entryIndex, long retainedContentStart) =>
        WithEntries(role, entries =>
            retainedContentStart > 0
            && entries.ContainsKey(entryIndex)
            && (!myContentTruncatedEntries.Contains((role, entryIndex))
                || myOptions.MaxArchivedEntryCharacters > myTruncationMarker.Length));

    internal RoleArchivedTranscriptEntry ReadEntry(string role, int entryIndex, long sequence)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            if (!myEntryLengths.TryGetValue(role, out var entries)
                || !entries.ContainsKey(entryIndex))
                return new RoleArchivedTranscriptEntry(
                    role,
                    sequence,
                    entryIndex,
                    null,
                    false,
                    0,
                    0);
            var contentTruncated = myContentTruncatedEntries.Contains((role, entryIndex));
            return new RoleArchivedTranscriptEntry(
                role,
                sequence,
                entryIndex,
                ReadEntryCore(role, entryIndex),
                contentTruncated,
                myTotalEntryLengths[(role, entryIndex)],
                contentTruncated
                    ? Math.Max(0, myOptions.MaxArchivedEntryCharacters - myTruncationMarker.Length)
                    : entries[entryIndex]);
        }
    }

    internal bool HasEntriesOutside(string role, IReadOnlyCollection<int> includedIndices) =>
        WithEntries(role, entries =>
        {
            var included = includedIndices.ToHashSet();
            return entries.Keys.Any(index => !included.Contains(index));
        });

    internal bool WasTruncated(string role)
    {
        lock (myStateLock)
            return myTruncatedRoles.Contains(role);
    }

    public void Dispose()
    {
        lock (myStateLock)
        {
            if (myDisposed)
                return;
            myDisposed = true;
            if (Directory.Exists(myDirectory))
                Directory.Delete(myDirectory, recursive: true);
        }
    }

    private SortedDictionary<int, int> GetEntries(string role)
    {
        if (!myEntryLengths.TryGetValue(role, out var entries))
            myEntryLengths[role] = entries = [];
        return entries;
    }

    private void WriteEntry(string role, int entryIndex, TranscriptEntry entry)
    {
        var directory = GetRoleDirectory(role);
        CreatePrivateDirectory(myDirectory);
        CreatePrivateDirectory(directory);
        WritePrivateText(
            GetMetadataPath(role, entryIndex),
            JsonSerializer.Serialize(new { entry.OccurredAt, entry.Source }),
            append: false);
        WritePrivateText(
            GetContentPath(role, entryIndex),
            LimitContent(entry.Content, myOptions.MaxArchivedEntryCharacters),
            append: false);
    }

    private void AppendContent(
        string role,
        string path,
        SortedDictionary<int, int> entries,
        int entryIndex,
        string content)
    {
        if (!entries.TryGetValue(entryIndex, out var currentLength))
            return;
        myTotalEntryLengths[(role, entryIndex)] =
            myTotalEntryLengths.GetValueOrDefault((role, entryIndex)) + content.Length;
        if (myContentTruncatedEntries.Contains((role, entryIndex)))
            return;
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
            retained += content[..Math.Min(content.Length, contentLimit - retained.Length)];
        var marker = myTruncationMarker[..Math.Min(myTruncationMarker.Length, maxCharacters)];
        WritePrivateText(path, retained[..Math.Min(retained.Length, contentLimit)] + marker, append: false);
        entries[entryIndex] = maxCharacters;
        myContentTruncatedEntries.Add((role, entryIndex));
        myTruncatedRoles.Add(role);
    }

    private TranscriptEntry ReadEntryCore(string role, int entryIndex)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(GetMetadataPath(role, entryIndex)));
        return new TranscriptEntry(
            metadata.RootElement.GetProperty("OccurredAt").GetDateTimeOffset(),
            metadata.RootElement.GetProperty("Source").GetString()!,
            File.ReadAllText(GetContentPath(role, entryIndex)));
    }

    private void EnforceLimits(string role, SortedDictionary<int, int> entries)
    {
        var totalCharacters = entries.Values.Sum();
        while (entries.Count > myOptions.MaxArchivedEntries
            || totalCharacters > myOptions.MaxArchivedContentCharacters)
        {
            var oldest = entries.First();
            File.Delete(GetMetadataPath(role, oldest.Key));
            File.Delete(GetContentPath(role, oldest.Key));
            entries.Remove(oldest.Key);
            myContentTruncatedEntries.Remove((role, oldest.Key));
            myTotalEntryLengths.Remove((role, oldest.Key));
            totalCharacters -= oldest.Value;
            myTruncatedRoles.Add(role);
        }
    }

    private string GetRoleDirectory(string role)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(role)));
        return Path.Combine(myDirectory, hash);
    }

    private string GetMetadataPath(string role, int entryIndex) =>
        Path.Combine(GetRoleDirectory(role), $"{entryIndex:D12}.json");

    private string GetContentPath(string role, int entryIndex) =>
        Path.Combine(GetRoleDirectory(role), $"{entryIndex:D12}.txt");

    private bool WithEntries(
        string role,
        Func<SortedDictionary<int, int>, bool> predicate)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            return myEntryLengths.TryGetValue(role, out var entries)
                && predicate(entries);
        }
    }

    private static string LimitContent(string content, int maxCharacters)
    {
        if (content.Length <= maxCharacters)
            return content;
        if (maxCharacters <= myTruncationMarker.Length)
            return myTruncationMarker[..maxCharacters];
        var contentLength = Math.Max(0, maxCharacters - myTruncationMarker.Length);
        return content[..contentLength] + myTruncationMarker;
    }

    private void RecordContentTruncation(string role, int entryIndex, string content)
    {
        if (content.Length > myOptions.MaxArchivedEntryCharacters)
        {
            myContentTruncatedEntries.Add((role, entryIndex));
            myTruncatedRoles.Add(role);
        }
        else
        {
            myContentTruncatedEntries.Remove((role, entryIndex));
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
                File.AppendAllText(path, content);
            else
                File.WriteAllText(path, content);
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



