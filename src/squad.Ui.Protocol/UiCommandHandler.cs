using System.Text.Json;
using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>
/// Validates UI command payloads and routes them to authoritative application or transcript operations. Protocol
/// framing and error publication remain the responsibility of <see cref="UiProtocolSession"/>.
/// </summary>
internal sealed class UiCommandHandler
{
    private const int myMaxTranscriptPageEntries = 200;
    private readonly ISquadUi myUi;
    private readonly ITranscriptUi myTranscriptUi;
    private readonly IIssueCatalog myIssueCatalog;
    private readonly IWorkspaceTools myWorkspaceTools;
    private readonly Action<string, object, string?> mySend;
    private readonly Action<
        bool,
        IReadOnlyDictionary<string, TranscriptSynchronizationPosition>?>
        myRequestTranscriptSynchronization;
    private readonly Action mySignalUiReady;

    internal UiCommandHandler(
        ISquadUi ui,
        ITranscriptUi transcriptUi,
        IIssueCatalog issueCatalog,
        IWorkspaceTools workspaceTools,
        Action<string, object, string?> send,
        Action<
            bool,
            IReadOnlyDictionary<string, TranscriptSynchronizationPosition>?>
            requestTranscriptSynchronization,
        Action signalUiReady)
    {
        myUi = ui;
        myTranscriptUi = transcriptUi;
        myIssueCatalog = issueCatalog;
        myWorkspaceTools = workspaceTools;
        mySend = send;
        myRequestTranscriptSynchronization =
            requestTranscriptSynchronization;
        mySignalUiReady = signalUiReady;
    }

    internal async Task HandleAsync(UiMessage message)
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
                mySend(
                    "workspace-tools.snapshot",
                    new { gitHistoryAvailable = myWorkspaceTools.GitHistoryAvailable },
                    null);
                mySignalUiReady();
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
                    TranscriptProtocol.CreatePagePayload(page),
                    null);
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
                    TranscriptProtocol.CreateArchivedEntryPayload(
                        archivedEntry),
                    null);
                break;
            case "prompt.send":
                await myUi.SendAsync(
                    RequireMemberId(message.Role),
                    RequirePayloadString(message.Payload, "prompt"));
                break;
            case "role.abort":
                await myUi.AbortAsync(RequireMemberId(message.Role));
                break;
            case "permission.respond":
                await myUi.CompletePermissionAsync(
                    RequireMemberId(message.Role),
                    Require(message.RequestId, "requestId"),
                    RequirePayloadBoolean(message.Payload, "approved"));
                break;
            case "input.respond":
                await myUi.CompleteInputAsync(
                    RequireMemberId(message.Role),
                    Require(message.RequestId, "requestId"),
                    GetPayloadString(message.Payload, "answer"),
                    GetPayloadBoolean(
                        message.Payload,
                        "wasFreeform",
                        true));
                break;
            case "elicitation.respond":
                var elicitationRole = RequireMemberId(message.Role);
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
                {
                    OpenExternalUrl(
                        Require(request.Url, "pending elicitation URL"));
                }
                break;
            case "issues.list":
                var issues = await myIssueCatalog.ListIssuesAsync();
                mySend(
                    "issues.list",
                    IssueProtocol.CreateListPayload(issues),
                    message.RequestId);
                break;
            case "git-history.open":
                if (!myWorkspaceTools.GitHistoryAvailable)
                {
                    throw new InvalidOperationException("Git history is not available.");
                }
                myWorkspaceTools.OpenGitHistory();
                break;
            default:
                mySend(
                    "protocol.error",
                    new
                    {
                        message =
                            $"Unknown UI message type '{message.Type}'.",
                    },
                    null);
                break;
        }
    }

    private static string Require(string? value, string property) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"The UI message is missing {property}.")
            : value;

    /// <summary>Validates and wraps an incoming wire role into a <see cref="SquadMemberId"/> exactly once, before
    /// it is dispatched to any authoritative application operation.</summary>
    private static SquadMemberId RequireMemberId(string? role) =>
        new(Require(role, "role"));

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
        {
            throw new InvalidOperationException(
                $"The UI message is missing payload.{property}.");
        }
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
        {
            throw new InvalidOperationException(
                $"The UI message is missing payload.{property}.");
        }
        return value;
    }

    private static IReadOnlyDictionary<string, TranscriptSynchronizationPosition>
        GetTranscriptSynchronizationPositions(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new Dictionary<string, TranscriptSynchronizationPosition>(
                StringComparer.Ordinal);
        }
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "The UI message contains an invalid transcript synchronization payload.");
        }
        if (!payload.TryGetProperty("roles", out var roles))
        {
            return new Dictionary<string, TranscriptSynchronizationPosition>(
                StringComparer.Ordinal);
        }
        if (roles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "The UI message contains invalid transcript positions.");
        }

        var positions =
            new Dictionary<string, TranscriptSynchronizationPosition>(
                StringComparer.Ordinal);
        foreach (var role in roles.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.Object
                || !role.TryGetProperty("role", out var roleName)
                || roleName.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(roleName.GetString()))
            {
                throw new InvalidOperationException(
                    "The UI message contains an invalid transcript position.");
            }
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
            {
                throw new InvalidOperationException(
                    "The UI message contains an invalid transcript position.");
            }
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
        {
            throw new InvalidOperationException(
                "The requested URL must be an absolute HTTP or HTTPS URL.");
        }
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
    }
}



