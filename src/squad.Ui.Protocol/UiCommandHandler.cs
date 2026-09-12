using System.Text.Json;
using squad.AgentProvider.Abstractions;
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
        IReadOnlyDictionary<SquadMemberId, TranscriptSynchronizationPosition>?>
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
            IReadOnlyDictionary<SquadMemberId, TranscriptSynchronizationPosition>?>
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
                        role => role.MemberId,
                        role => new TranscriptSynchronizationPosition(
                            role.Sequence,
                            role.Sequence));
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
                var pageMemberId = RequireMemberId(message.Role);
                var beforeIndex = RequirePayloadInt32(
                    message.Payload,
                    "beforeIndex");
                var page = myTranscriptUi.CreateTranscriptPage(
                    pageMemberId,
                    beforeIndex,
                    myMaxTranscriptPageEntries);
                mySend(
                    "transcript.page",
                    TranscriptProtocol.CreatePagePayload(page),
                    null);
                break;
            case "transcript.entry":
                var entryMemberId = RequireMemberId(message.Role);
                var entryIndex = RequirePayloadInt32(
                    message.Payload,
                    "entryIndex");
                var archivedEntry =
                    myTranscriptUi.CreateArchivedTranscriptEntry(
                        entryMemberId,
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
                    RequireRequestId(message.RequestId),
                    RequirePayloadBoolean(message.Payload, "approved"));
                break;
            case "input.respond":
                await myUi.CompleteInputAsync(
                    RequireMemberId(message.Role),
                    RequireRequestId(message.RequestId),
                    GetPayloadString(message.Payload, "answer"),
                    GetPayloadBoolean(
                        message.Payload,
                        "wasFreeform",
                        true));
                break;
            case "elicitation.respond":
                var elicitationRole = RequireMemberId(message.Role);
                var elicitationId = RequireRequestId(message.RequestId);
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
                if (action == "accept" && request.Mode == ElicitationMode.Url)
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

    /// <summary>Validates and wraps an incoming wire requestId into an <see cref="InteractionRequestId"/> exactly
    /// once, before it is dispatched to any authoritative application operation.</summary>
    private static InteractionRequestId RequireRequestId(string? requestId) =>
        new(Require(requestId, "requestId"));

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

    private static IReadOnlyDictionary<SquadMemberId, TranscriptSynchronizationPosition>
        GetTranscriptSynchronizationPositions(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new Dictionary<SquadMemberId, TranscriptSynchronizationPosition>();
        }
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "The UI message contains an invalid transcript synchronization payload.");
        }
        if (!payload.TryGetProperty("roles", out var roles))
        {
            return new Dictionary<SquadMemberId, TranscriptSynchronizationPosition>();
        }
        if (roles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "The UI message contains invalid transcript positions.");
        }

        var positions = new Dictionary<SquadMemberId, TranscriptSynchronizationPosition>();
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
            positions[new SquadMemberId(roleName.GetString()!)] = new(
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



