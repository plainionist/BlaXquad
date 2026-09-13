using System.Text.Json;

namespace squad.Specs.Support.Ui;

/// <summary>
/// Owns transcript envelope matching, decoding, paging, and update/synchronization reconciliation for the
/// headless UI protocol: recognizing "transcript.update", "transcript.page", "transcript.entry", and
/// "transcript.synchronize" envelopes, decoding their typed fields into <see cref="TranscriptUpdateObservation"/>
/// and <see cref="TranscriptEntryObservation"/>, and replaying updates over a synchronization's seeded entries in
/// publication order exactly as a reconnecting dashboard client must - an "append" adds its entry at its reported
/// index, an "append-content" appends its delta fragment onto the entry already at its index, and a "replace"
/// overwrites the entry at its index outright. Carries no waiting, timeout, or transport concerns of its own -
/// <see cref="HeadlessUiClient"/> supplies the captured protocol lines and owns every non-transcript protocol
/// predicate (role state, usage, tool, and pending-interaction matching).
/// </summary>
internal static class TranscriptProtocol
{
    public static bool IsTranscriptUpdate(JsonElement element, string role, string content)
    {

        if (!IsType(element, "transcript.update"))
        {
            return false;
        }

        var payload = GetPayload(element);
        return payload.TryGetProperty("role", out var roleElement) && roleElement.GetString() == role
            && payload.TryGetProperty("entry", out var entry)
            && entry.ValueKind == JsonValueKind.Object
            && entry.TryGetProperty("content", out var contentElement)
            && contentElement.GetString() == content;
    }

    public static bool IsMatchingTranscriptEntryUpdate(JsonElement element, string role, string source, string? content)
    {

        if (!IsType(element, "transcript.update"))
        {
            return false;
        }

        var payload = GetPayload(element);

        if (!payload.TryGetProperty("role", out var roleElement) || roleElement.GetString() != role)
        {
            return false;
        }

        if (!payload.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!entry.TryGetProperty("source", out var sourceElement) || sourceElement.GetString() != source)
        {
            return false;
        }

        return content is null
            || (entry.TryGetProperty("content", out var contentElement) && contentElement.GetString() == content);
    }

    public static bool IsMatchingTranscriptOperationUpdate(JsonElement element, string role, string operation, string? content)
    {

        if (!IsType(element, "transcript.update"))
        {
            return false;
        }

        var payload = GetPayload(element);

        if (!payload.TryGetProperty("role", out var roleElement) || roleElement.GetString() != role)
        {
            return false;
        }

        if (!payload.TryGetProperty("operation", out var operationElement) || operationElement.GetString() != operation)
        {
            return false;
        }

        return content is null || ResolveTranscriptUpdateContent(payload) == content;
    }

    public static string? ResolveTranscriptUpdateContent(JsonElement payload)
    {

        if (payload.TryGetProperty("entry", out var entry) && entry.ValueKind == JsonValueKind.Object
            && entry.TryGetProperty("content", out var entryContentElement) && entryContentElement.ValueKind == JsonValueKind.String)
        {

            return entryContentElement.GetString();
        }

        if (payload.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString();
        }

        return null;
    }

    public static TranscriptUpdateObservation ParseTranscriptUpdate(JsonElement element)
    {
        var payload = GetPayload(element);
        var role = payload.GetProperty("role").GetString()!;
        var sequence = payload.GetProperty("sequence").GetInt64();
        var operation = payload.GetProperty("operation").GetString()!;
        var entryIndex = payload.GetProperty("entryIndex").GetInt32();
        var hasEntry = payload.TryGetProperty("entry", out var entry) && entry.ValueKind == JsonValueKind.Object;
        var source = hasEntry
            && entry.TryGetProperty("source", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.String
            ? sourceElement.GetString()
            : null;
        var hasArchivedContent = hasEntry
            && entry.TryGetProperty("hasArchivedContent", out var hasArchivedContentElement)
            && hasArchivedContentElement.GetBoolean();
        var contentStart = hasEntry && entry.TryGetProperty("contentStart", out var contentStartElement)
            ? contentStartElement.GetInt64()
            : 0;
        var content = ResolveTranscriptUpdateContent(payload);
        var hasAnnouncement = payload.TryGetProperty("announcement", out var announcement) && announcement.ValueKind == JsonValueKind.Object;
        var announcementTruncated = hasAnnouncement
            && announcement.TryGetProperty("truncated", out var announcementTruncatedElement)
            && announcementTruncatedElement.GetBoolean();
        var announcementContentLength = hasAnnouncement
            && announcement.TryGetProperty("content", out var announcementContentElement)
            && announcementContentElement.ValueKind == JsonValueKind.String
            ? announcementContentElement.GetString()!.Length
            : (int?)null;
        return new TranscriptUpdateObservation(
            role,
            sequence,
            operation,
            entryIndex,
            source,
            content,
            hasArchivedContent,
            contentStart,
            announcementTruncated,
            announcementContentLength);
    }

    public static bool TryGetTranscriptSynchronizationEntries(
        JsonElement element, string role, out IReadOnlyList<TranscriptEntryObservation> entries)
    {
        entries = [];

        if (!IsType(element, "transcript.synchronize"))
        {
            return false;
        }

        if (!GetPayload(element).TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var roleElement in roles.EnumerateArray())
        {

            if (!roleElement.TryGetProperty("role", out var name) || name.GetString() != role)
            {
                continue;
            }

            entries = ParseTranscriptEntries(roleElement);
            return true;
        }

        return false;
    }

    /// <summary>Reconciles the most recently published "transcript.synchronize" message for the role (its
    /// entries seed the result, its sequence is the high-water mark) with every "transcript.update" message for
    /// the role published afterward, applied in publication order. Returns false only if no synchronization for
    /// the role has been published yet.</summary>
    public static bool TryReconcileTranscript(
        IReadOnlyList<string> stdOutLines, string role, out IReadOnlyList<TranscriptEntryObservation> entries)
    {
        entries = [];
        JsonElement? latestSynchronization = null;

        foreach (var line in stdOutLines)
        {
            using var document = JsonDocument.Parse(line);

            if (TryGetTranscriptSynchronizationEntries(document.RootElement, role, out _))
            {
                latestSynchronization = document.RootElement.Clone();
            }

        }

        if (latestSynchronization is not { } synchronization)

        {
            return false;
        }

        TryGetTranscriptSynchronizationEntries(synchronization, role, out var seededEntries);
        var highWaterMark = GetRoleSynchronizationSequence(synchronization, role);

        var reconciled = new SortedDictionary<int, (string Source, string Content)>();

        foreach (var entry in seededEntries)
        {
            reconciled[entry.EntryIndex] = (entry.Source, entry.Content);
        }

        foreach (var line in stdOutLines)
        {
            using var document = JsonDocument.Parse(line);
            var element = document.RootElement;

            if (!IsType(element, "transcript.update"))
            {
                continue;
            }

            var payload = GetPayload(element);

            if (!payload.TryGetProperty("role", out var roleElement) || roleElement.GetString() != role
                || payload.GetProperty("sequence").GetInt64() <= highWaterMark)
            {

                continue;
            }

            var entryIndex = payload.GetProperty("entryIndex").GetInt32();

            switch (payload.GetProperty("operation").GetString())
            {
                case "append":
                case "replace":
                    var entry = payload.GetProperty("entry");
                    reconciled[entryIndex] = (entry.GetProperty("source").GetString()!, entry.GetProperty("content").GetString()!);
                    break;
                case "append-content":

                    if (reconciled.TryGetValue(entryIndex, out var existing))
                    {
                        reconciled[entryIndex] = (existing.Source, existing.Content + payload.GetProperty("content").GetString());
                    }

                    break;
            }
        }

        entries = reconciled.Select(pair => new TranscriptEntryObservation(pair.Key, pair.Value.Source, pair.Value.Content)).ToList();
        return true;
    }

    public static IReadOnlyList<TranscriptEntryObservation> ParseTranscriptEntries(JsonElement roleElement)
    {

        if (!roleElement.TryGetProperty("entries", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var entries = new List<TranscriptEntryObservation>();

        foreach (var entry in entriesElement.EnumerateArray())
        {
            var entryIndex = entry.GetProperty("entryIndex").GetInt32();
            var source = entry.TryGetProperty("source", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.String
                ? sourceElement.GetString()!
                : "";
            var content = entry.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString()!
                : "";
            entries.Add(new TranscriptEntryObservation(entryIndex, source, content));
        }

        return entries;
    }

    public static long GetRoleSynchronizationSequence(JsonElement element, string role)
    {

        if (!GetPayload(element).TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        foreach (var roleElement in roles.EnumerateArray())
        {

            if (roleElement.TryGetProperty("role", out var name) && name.GetString() == role)
            {
                return roleElement.TryGetProperty("sequence", out var sequenceElement) ? sequenceElement.GetInt64() : 0;
            }

        }

        return 0;
    }

    public static bool IsTranscriptPageForRole(JsonElement element, string role)
    {

        if (!IsType(element, "transcript.page"))
        {
            return false;
        }

        var payload = GetPayload(element);
        return payload.TryGetProperty("role", out var roleElement) && roleElement.GetString() == role;
    }

    public static bool IsArchivedEntryForRoleAndIndex(JsonElement element, string role, int entryIndex)
    {

        if (!IsType(element, "transcript.entry"))
        {
            return false;
        }

        var payload = GetPayload(element);
        return payload.TryGetProperty("role", out var roleElement) && roleElement.GetString() == role
            && payload.TryGetProperty("entryIndex", out var entryIndexElement) && entryIndexElement.GetInt32() == entryIndex;
    }

    private static bool IsType(JsonElement element, string type) =>
        element.TryGetProperty("type", out var typeElement) && typeElement.GetString() == type;

    private static JsonElement GetPayload(JsonElement element) => element.GetProperty("payload");
}
