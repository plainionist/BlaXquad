using System.Diagnostics;
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
    private readonly Dictionary<SquadMemberId, RoleArchiveState> myRoles = [];
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
            var role = GetRole(update.MemberId);
            var path = GetContentPath(update.MemberId, update.EntryIndex);

            switch (update.Kind)
            {
                case TranscriptUpdateKind.AppendEntry:
                case TranscriptUpdateKind.ReplaceEntry:
                    WriteEntry(update.MemberId, update.EntryIndex, update.Entry!);
                    var state = CreateEntryState(update.Entry!, myOptions.MaxArchivedEntryCharacters);
                    role.Entries[update.EntryIndex] = state;

                    if (state.ContentTruncated)
                    {
                        role.MarkTruncated();
                    }

                    break;
                case TranscriptUpdateKind.AppendContent:
                    AppendContent(path, role, update.EntryIndex, update.Content!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(update.Kind));
            }

            EnforceLimits(update.MemberId, role);
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

            if (!myRoles.TryGetValue(memberId, out var role))
            {
                return [];
            }

            return role.Entries.Keys
                .Where(index => index < beforeIndex)
                .TakeLast(maxEntries)
                .Select(index => new IndexedTranscriptEntry(index, ReadEntryCore(memberId, index)))
                .ToArray();
        }
    }

    internal bool HasEntriesBefore(SquadMemberId memberId, int beforeIndex) =>
        WithRole(memberId, role => role.Entries.Keys.Any(index => index < beforeIndex));

    internal bool HasMoreContent(SquadMemberId memberId, int entryIndex, long retainedContentStart) =>
        WithRole(memberId, role =>
            retainedContentStart > 0
            && role.Entries.TryGetValue(entryIndex, out var state)
            && (!state.ContentTruncated
                || myOptions.MaxArchivedEntryCharacters > myTruncationMarker.Length));

    internal RoleArchivedTranscriptEntry ReadEntry(SquadMemberId memberId, int entryIndex, long sequence)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);

            if (!myRoles.TryGetValue(memberId, out var role)
                || !role.Entries.TryGetValue(entryIndex, out var state))
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

            return new RoleArchivedTranscriptEntry(
                memberId,
                sequence,
                entryIndex,
                ReadEntryCore(memberId, entryIndex),
                state.ContentTruncated,
                state.TotalLength,
                state.ContentTruncated
                    ? Math.Max(0, myOptions.MaxArchivedEntryCharacters - myTruncationMarker.Length)
                    : state.RetainedLength);
        }
    }

    internal bool HasEntriesOutside(SquadMemberId memberId, IReadOnlyCollection<int> includedIndices) =>
        WithRole(memberId, role =>
        {
            var included = includedIndices.ToHashSet();
            return role.Entries.Keys.Any(index => !included.Contains(index));
        });

    internal bool WasTruncated(SquadMemberId memberId)
    {
        lock (myStateLock)
            return myRoles.TryGetValue(memberId, out var role) && role.Truncated;
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

    private RoleArchiveState GetRole(SquadMemberId memberId)
    {

        if (!myRoles.TryGetValue(memberId, out var role))
        {
            myRoles[memberId] = role = new RoleArchiveState();
        }

        return role;
    }

    private void WriteEntry(SquadMemberId memberId, int entryIndex, TranscriptEntry entry)
    {
        var directory = GetRoleDirectory(memberId);
        CreatePrivateDirectory(myDirectory);
        CreatePrivateDirectory(directory);
        WritePrivateText(
            GetMetadataPath(memberId, entryIndex),
            JsonSerializer.Serialize(new { entry.OccurredAt, Source = ToArchiveSource(entry.Source) }),
            append: false);
        WritePrivateText(
            GetContentPath(memberId, entryIndex),
            LimitContent(entry.Content, myOptions.MaxArchivedEntryCharacters),
            append: false);
    }

    private void AppendContent(
        string path,
        RoleArchiveState role,
        int entryIndex,
        string content)
    {

        if (!role.Entries.TryGetValue(entryIndex, out var state))
        {
            return;
        }

        state = state.WithAddedTotalLength(content.Length);

        if (state.ContentTruncated)
        {
            role.Entries[entryIndex] = state;
            return;
        }

        var maxCharacters = myOptions.MaxArchivedEntryCharacters;

        if (content.Length <= maxCharacters - state.RetainedLength)
        {
            WritePrivateText(path, content, append: true);
            role.Entries[entryIndex] = state.WithRetainedLength(state.RetainedLength + content.Length);
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
        role.Entries[entryIndex] = state.WithRetainedLength(maxCharacters).WithContentTruncated();
        role.MarkTruncated();
    }

    private TranscriptEntry ReadEntryCore(SquadMemberId memberId, int entryIndex)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(GetMetadataPath(memberId, entryIndex)));
        return new TranscriptEntry(
            metadata.RootElement.GetProperty("OccurredAt").GetDateTimeOffset(),
            ParseArchiveSource(metadata.RootElement.GetProperty("Source").GetString()!),
            File.ReadAllText(GetContentPath(memberId, entryIndex)));
    }

    private void EnforceLimits(SquadMemberId memberId, RoleArchiveState role)
    {
        var totalCharacters = role.Entries.Values.Sum(state => state.RetainedLength);

        while (role.Entries.Count > myOptions.MaxArchivedEntries
            || totalCharacters > myOptions.MaxArchivedContentCharacters)
        {

            var oldest = role.Entries.First();
            File.Delete(GetMetadataPath(memberId, oldest.Key));
            File.Delete(GetContentPath(memberId, oldest.Key));
            role.Entries.Remove(oldest.Key);
            totalCharacters -= oldest.Value.RetainedLength;
            role.MarkTruncated();
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

    private bool WithRole(
        SquadMemberId memberId,
        Func<RoleArchiveState, bool> predicate)
    {
        lock (myStateLock)
        {
            ObjectDisposedException.ThrowIf(myDisposed, this);
            return myRoles.TryGetValue(memberId, out var role)
                && predicate(role);
        }
    }

    /// <summary>Maps <see cref="TranscriptSource"/> explicitly to its stable archived spelling, so the on-disk
    /// representation never depends on default enum serialization.</summary>
    private static string ToArchiveSource(TranscriptSource source) => source switch
    {
        TranscriptSource.Harness => "harness",
        TranscriptSource.User => "user",
        TranscriptSource.Assistant => "assistant",
        TranscriptSource.Reasoning => "reasoning",
        TranscriptSource.System => "system",
        TranscriptSource.Error => "error",
        TranscriptSource.Tool => "tool",
        TranscriptSource.Read => "read",
        TranscriptSource.Subagent => "subagent",
        _ => throw new UnreachableException($"Unhandled transcript source '{source}'."),
    };

    /// <summary>Parses an archived source spelling back into <see cref="TranscriptSource"/>, failing explicitly for
    /// any value this archive format does not recognize.</summary>
    private static TranscriptSource ParseArchiveSource(string source) => source switch
    {
        "harness" => TranscriptSource.Harness,
        "user" => TranscriptSource.User,
        "assistant" => TranscriptSource.Assistant,
        "reasoning" => TranscriptSource.Reasoning,
        "system" => TranscriptSource.System,
        "error" => TranscriptSource.Error,
        "tool" => TranscriptSource.Tool,
        "read" => TranscriptSource.Read,
        "subagent" => TranscriptSource.Subagent,
        _ => throw new InvalidOperationException($"Unsupported archived transcript source '{source}'."),
    };

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

    private static ArchivedEntryState CreateEntryState(TranscriptEntry entry, int maxCharacters) =>
        new(
            Math.Min(entry.Content.Length, maxCharacters),
            entry.Content.Length,
            entry.Content.Length > maxCharacters);

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
