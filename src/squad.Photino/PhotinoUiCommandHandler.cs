using System.Text.Json;
using squad.Ui.Abstractions;

namespace squad.Photino;

internal sealed class PhotinoUiCommandHandler
{
    private const int myMaxTranscriptPageEntries = 200;
    private readonly ISquadUi myUi;
    private readonly ITranscriptUi myTranscriptUi;
    private readonly Action<string, object> mySend;
    private readonly Action<
        bool,
        IReadOnlyDictionary<string, TranscriptSynchronizationPosition>?>
        myRequestTranscriptSynchronization;
    private readonly Action mySignalUiReady;
    private readonly Action myRequestSmokeShutdown;
    private readonly Action<string> myOpenExternalUrl;

    internal PhotinoUiCommandHandler(
        ISquadUi ui,
        ITranscriptUi transcriptUi,
        Action<string, object> send,
        Action<
            bool,
            IReadOnlyDictionary<string, TranscriptSynchronizationPosition>?>
            requestTranscriptSynchronization,
        Action signalUiReady,
        Action requestSmokeShutdown,
        Action<string>? openExternalUrl)
    {
        myUi = ui;
        myTranscriptUi = transcriptUi;
        mySend = send;
        myRequestTranscriptSynchronization =
            requestTranscriptSynchronization;
        mySignalUiReady = signalUiReady;
        myRequestSmokeShutdown = requestSmokeShutdown;
        myOpenExternalUrl = openExternalUrl ?? OpenExternalUrl;
    }

    internal async Task HandleAsync(PhotinoUiMessage message)
    {
        switch (message.Type)
        {
            case "ui.ready":
                var initialPositions = myTranscriptUi
                    .CreateTranscriptSnapshot(1)
                    .ToDictionary(
                        role => role.Role,
                        role => new TranscriptSynchronizationPosition(
                            role.Sequence,
                            role.Sequence),
                        StringComparer.Ordinal);
                myRequestTranscriptSynchronization(true, initialPositions);
                mySignalUiReady();
                if (Environment.GetEnvironmentVariable(
                        "BLAXQUAD_PHOTINO_SMOKE") == "1")
                    myRequestSmokeShutdown();
                break;
            case "transcript.synchronize":
                myRequestTranscriptSynchronization(
                    false,
                    GetTranscriptSynchronizationPositions(message.Payload));
                break;
            case "transcript.page":
                var pageRole = Require(message.Role, "role");
                var beforeIndex = RequirePayloadInt32(
                    message.Payload,
                    "beforeIndex");
                var page = myTranscriptUi.CreateTranscriptPage(
                    pageRole,
                    beforeIndex,
                    myMaxTranscriptPageEntries);
                mySend(
                    "transcript.page",
                    PhotinoTranscriptProtocol.CreatePagePayload(page));
                break;
            case "transcript.entry":
                var entryRole = Require(message.Role, "role");
                var entryIndex = RequirePayloadInt32(
                    message.Payload,
                    "entryIndex");
                var archivedEntry =
                    myTranscriptUi.CreateArchivedTranscriptEntry(
                        entryRole,
                        entryIndex);
                mySend(
                    "transcript.entry",
                    PhotinoTranscriptProtocol.CreateArchivedEntryPayload(
                        archivedEntry));
                break;
            case "prompt.send":
                await myUi.SendAsync(
                    Require(message.Role, "role"),
                    RequirePayloadString(message.Payload, "prompt"));
                break;
            case "role.abort":
                await myUi.AbortAsync(Require(message.Role, "role"));
                break;
            case "permission.respond":
                await myUi.CompletePermissionAsync(
                    Require(message.Role, "role"),
                    Require(message.RequestId, "requestId"),
                    RequirePayloadBoolean(message.Payload, "approved"));
                break;
            case "input.respond":
                await myUi.CompleteInputAsync(
                    Require(message.Role, "role"),
                    Require(message.RequestId, "requestId"),
                    GetPayloadString(message.Payload, "answer"),
                    GetPayloadBoolean(
                        message.Payload,
                        "wasFreeform",
                        true));
                break;
            case "elicitation.respond":
                var elicitationRole = Require(message.Role, "role");
                var elicitationId = Require(
                    message.RequestId,
                    "requestId");
                var action = RequirePayloadString(
                    message.Payload,
                    "action");
                var request = myUi.GetPendingElicitation(
                    elicitationRole,
                    elicitationId);
                await myUi.CompleteElicitationAsync(
                    elicitationRole,
                    elicitationId,
                    action,
                    GetPayloadElement(message.Payload, "content"));
                if (action == "accept" && request.Mode == "url")
                    myOpenExternalUrl(
                        Require(request.Url, "pending elicitation URL"));
                break;
            default:
                mySend(
                    "protocol.error",
                    new
                    {
                        message =
                            $"Unknown UI message type '{message.Type}'.",
                    });
                break;
        }
    }

    private static string Require(string? value, string property) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"The UI message is missing {property}.")
            : value;

    private static string RequirePayloadString(
        JsonElement payload,
        string property) =>
        Require(GetPayloadString(payload, property), $"payload.{property}");

    private static string? GetPayloadString(
        JsonElement payload,
        string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool RequirePayloadBoolean(
        JsonElement payload,
        string property)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(property, out var element)
            || element.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException(
                $"The UI message is missing payload.{property}.");
        return element.GetBoolean();
    }

    private static int RequirePayloadInt32(
        JsonElement payload,
        string property)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(property, out var element)
            || !element.TryGetInt32(out var value)
            || value < 0)
            throw new InvalidOperationException(
                $"The UI message is missing payload.{property}.");
        return value;
    }

    private static IReadOnlyDictionary<string, TranscriptSynchronizationPosition>
        GetTranscriptSynchronizationPositions(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return new Dictionary<string, TranscriptSynchronizationPosition>(
                StringComparer.Ordinal);
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "The UI message contains an invalid transcript synchronization payload.");
        if (!payload.TryGetProperty("roles", out var roles))
            return new Dictionary<string, TranscriptSynchronizationPosition>(
                StringComparer.Ordinal);
        if (roles.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                "The UI message contains invalid transcript positions.");

        var positions =
            new Dictionary<string, TranscriptSynchronizationPosition>(
                StringComparer.Ordinal);
        foreach (var role in roles.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.Object
                || !role.TryGetProperty("role", out var roleName)
                || roleName.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(roleName.GetString()))
                throw new InvalidOperationException(
                    "The UI message contains an invalid transcript position.");
            var hasLegacySequence = role.TryGetProperty(
                "sequence",
                out var legacySequence);
            var hasVisualSequence = role.TryGetProperty(
                "visualSequence",
                out var visualSequence);
            var hasAnnouncementSequence = role.TryGetProperty(
                "announcementSequence",
                out var announcementSequence);
            if ((!hasVisualSequence && !hasLegacySequence)
                || !(hasVisualSequence ? visualSequence : legacySequence)
                    .TryGetInt64(out var visualValue)
                || visualValue < 0
                || (!hasAnnouncementSequence && !hasLegacySequence)
                || !(hasAnnouncementSequence
                        ? announcementSequence
                        : legacySequence)
                    .TryGetInt64(out var announcementValue)
                || announcementValue < 0)
                throw new InvalidOperationException(
                    "The UI message contains an invalid transcript position.");
            positions[roleName.GetString()!] = new(
                visualValue,
                announcementValue);
        }
        return positions;
    }

    private static bool GetPayloadBoolean(
        JsonElement payload,
        string property,
        bool defaultValue) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var element)
        && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : defaultValue;

    private static JsonElement? GetPayloadElement(
        JsonElement payload,
        string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var element)
            ? element.Clone()
            : null;

    private static void OpenExternalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(
                "The requested URL must be an absolute HTTP or HTTPS URL.");
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
    }
}




