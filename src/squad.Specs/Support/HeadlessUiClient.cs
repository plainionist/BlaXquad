using System.Text.Json;
using System.Linq;

namespace squad.Specs.Support;

/// <summary>
/// Semantic client for one launched "squad-hq --ui stdio" process. It owns the process's standard input, drains
/// standard output and standard error concurrently with two independent background readers so a full stderr pipe
/// can never block a pending stdout read (or vice versa), and privately frames the newline-delimited, versioned UI
/// protocol envelopes. Step definitions see only readiness, prompt sending, abort/interaction-response sending,
/// role-status/usage waiting, transcript waiting, and protocol-error reporting - never raw JSON, envelopes,
/// streams, or the child process itself.
/// </summary>
public sealed class HeadlessUiClient
{
    private const int ProtocolVersion = 3;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly System.Diagnostics.Process myProcess;
    private readonly object myLinesLock = new();
    private readonly List<string> myStdOutLines = [];
    private readonly List<string> myStdErrLines = [];

    public HeadlessUiClient(System.Diagnostics.Process process)
    {
        myProcess = process;
        Drain(process.StandardOutput, myStdOutLines);
        Drain(process.StandardError, myStdErrLines);
    }

    /// <summary>
    /// Sends "ui.ready" and waits for the real handshake response - the initial "state.snapshot" message - proving
    /// the process completed startup and began publishing protocol state.
    /// </summary>
    public Task CompleteReadyHandshakeAsync(TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        SendEnvelope("ui.ready");
        return WaitForMessageAsync(
            IsStateSnapshot, "the ui.ready handshake to produce a state.snapshot message", timeout, additionalDiagnostics);
    }

    /// <summary>Sends a prompt for the given role through the real "prompt.send" command.</summary>
    public void SendPrompt(string role, string prompt) => SendEnvelope("prompt.send", role, new { prompt });

    /// <summary>Aborts the given role's current operation through the real "role.abort" command.</summary>
    public void SendAbort(string role) => SendEnvelope("role.abort", role);

    /// <summary>Responds to a permission request through the real "permission.respond" command.</summary>
    public void RespondToPermission(string role, string requestId, bool approved) =>
        SendEnvelope("permission.respond", role, new { approved }, requestId);

    /// <summary>Responds to an input request through the real "input.respond" command.</summary>
    public void RespondToInput(string role, string requestId, string? answer, bool wasFreeform) =>
        SendEnvelope("input.respond", role, new { answer, wasFreeform }, requestId);

    /// <summary>Responds to an elicitation request through the real "elicitation.respond" command, optionally
    /// carrying accepted content (for example a form value) alongside the chosen action.</summary>
    public void RespondToElicitation(string role, string requestId, string action, object? content = null) =>
        SendEnvelope("elicitation.respond", role, new { action, content }, requestId);

    /// <summary>Requests a fresh full transcript synchronization through the real "transcript.synchronize"
    /// command, producing a new "transcript.synchronize" message alongside every currently retained entry for
    /// every role - the same message a reconnecting dashboard relies on to rebuild its view.</summary>
    public void RequestTranscriptSynchronization() => SendEnvelope("transcript.synchronize");

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given status.</summary>
    public Task WaitForRoleStatusAsync(string role, string status, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element) && RoleHasStatus(element, role, status),
            $"role '{role}' to report status '{status}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given AI-credit usage.</summary>
    public Task WaitForRoleUsageAsync(
        string role, decimal aicUsed, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element) && RoleHasAicUsed(element, role, aicUsed),
            $"role '{role}' to report AI-credit usage '{aicUsed}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until a "transcript.update" message reports the given content for the given role.</summary>
    public Task WaitForTranscriptAsync(string role, string content, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsTranscriptUpdate(element, role, content),
            $"a transcript update for role '{role}' with content '{content}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until a "transcript.update" message reports an appended or replaced entry for the given
    /// role with the given source - and, unless null, the given content - and returns the dashboard protocol's
    /// typed <c>sequence</c>, <c>operation</c>, <c>entryIndex</c>, <c>source</c>, and <c>content</c> fields.</summary>
    public async Task<TranscriptUpdateObservation> WaitForTranscriptUpdateAsync(
        string role, string source, string? content = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var description = content is null
            ? $"a transcript update for role '{role}' with source '{source}'"
            : $"a transcript update for role '{role}' with source '{source}' and content '{content}'";
        var element = await WaitForMessageAsync(
            transcriptUpdate => IsMatchingTranscriptEntryUpdate(transcriptUpdate, role, source, content),
            description,
            timeout,
            additionalDiagnostics);
        return ParseTranscriptUpdate(element);
    }

    /// <summary>Waits until a "transcript.update" message reports the given operation for the given role - and,
    /// unless null, the given content - and returns the dashboard protocol's typed fields. Unlike
    /// <see cref="WaitForTranscriptUpdateAsync(string,string,string?,TimeSpan?,Func{string}?)"/> - which matches by
    /// source and therefore only ever observes an appended or replaced entry - this resolves content from either
    /// the appended/replaced entry or an "append-content" update's top-level delta fragment, so it can also
    /// observe a streamed continuation that carries no source of its own.</summary>
    public async Task<TranscriptUpdateObservation> WaitForTranscriptUpdateByOperationAsync(
        string role, string operation, string? content = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var description = content is null
            ? $"a transcript update for role '{role}' with operation '{operation}'"
            : $"a transcript update for role '{role}' with operation '{operation}' and content '{content}'";
        var element = await WaitForMessageAsync(
            transcriptUpdate => IsMatchingTranscriptOperationUpdate(transcriptUpdate, role, operation, content),
            description,
            timeout,
            additionalDiagnostics);
        return ParseTranscriptUpdate(element);
    }

    /// <summary>Waits until the most recently published "transcript.synchronize" message includes a role entry for
    /// the given role whose decoded entries satisfy the given predicate, and returns the dashboard protocol's typed
    /// <c>sequence</c> and indexed, sourced <c>entries</c> fields for that role.</summary>
    public async Task<TranscriptSynchronizationObservation> WaitForTranscriptSynchronizationAsync(
        string role,
        Func<IReadOnlyList<TranscriptEntryObservation>, bool> matches,
        TimeSpan? timeout = null,
        Func<string>? additionalDiagnostics = null)
    {
        var element = await WaitForMessageAsync(
            transcriptSynchronize => TryGetTranscriptSynchronizationEntries(transcriptSynchronize, role, out var entries) && matches(entries),
            $"a transcript synchronization for role '{role}' matching the expected entries",
            timeout,
            additionalDiagnostics);
        TryGetTranscriptSynchronizationEntries(element, role, out var observedEntries);
        return new TranscriptSynchronizationObservation(role, GetRoleSynchronizationSequence(element, role), observedEntries);
    }

    /// <summary>Waits until a "state.snapshot" message publishes a pending permission request with the given
    /// role, request id, and description.</summary>
    public Task WaitForPendingPermissionAsync(
        string role, string requestId, string description, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element) && HasPendingPermission(element, role, requestId, description),
            $"a pending permission '{requestId}' for role '{role}' with description '{description}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until the most recently published "state.snapshot" message no longer publishes the given
    /// pending permission request for the given role - proving a terminal role failure genuinely removed it,
    /// rather than merely matching an earlier snapshot recorded before the request ever existed (for example the
    /// initial "ui.ready" handshake snapshot).</summary>
    public Task WaitForNoPendingPermissionAsync(
        string role, string requestId, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForLatestStateSnapshotAsync(
            element => !HasPendingPermissionWithId(element, role, requestId),
            $"role '{role}' to no longer publish a pending permission '{requestId}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until a "state.snapshot" message publishes a pending input request with the given role,
    /// request id, prompt, choices, and freeform support.</summary>
    public Task WaitForPendingInputAsync(
        string role, string requestId, string prompt, IReadOnlyList<string>? choices, bool allowFreeform,
        TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element) && HasPendingInput(element, role, requestId, prompt, choices, allowFreeform),
            $"a pending input '{requestId}' for role '{role}' with prompt '{prompt}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until a "state.snapshot" message publishes a pending elicitation request with the given
    /// role, request id, prompt, mode, and URL (or null if the mode does not carry one).</summary>
    public Task WaitForPendingElicitationAsync(
        string role, string requestId, string prompt, string mode, string? url = null,
        TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element) && HasPendingElicitation(element, role, requestId, prompt, mode, url),
            $"a pending elicitation '{requestId}' for role '{role}' with prompt '{prompt}' and mode '{mode}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits for a "protocol.error" message and returns its human-readable message. <paramref name="skip"/>
    /// skips that many earlier matching messages already observed, so a caller can require the next distinct
    /// occurrence produced by a later command instead of re-matching an earlier one.</summary>
    public async Task<string> WaitForProtocolErrorAsync(int skip = 0, TimeSpan? timeout = null)
    {
        var element = await WaitForMessageAsync(IsProtocolError, "a protocol.error message", timeout, additionalDiagnostics: null, skip);
        return GetPayload(element).TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()!
            : throw new InvalidOperationException("The protocol.error message did not include a message.");
    }

    /// <summary>
    /// Builds a diagnostics snapshot of the launched process's command/lifecycle state, its captured standard
    /// output and standard error, and the most recently observed UI state. Exposed so callers beyond this
    /// client's own semantic waits (for example lifecycle cleanup awaiting process exit) can report the same
    /// bounded-wait diagnostics without exposing the process, raw JSON, or protocol envelopes themselves.
    /// </summary>
    public string DescribeDiagnostics() => DescribeDiagnostics(CopyLines(myStdOutLines), additionalDiagnostics: null);

    /// <summary>Same as the parameterless overload, but appends the given caller-supplied diagnostics (for
    /// example combined provider/control-pipe observations) to the same single diagnostics block.</summary>
    public string DescribeDiagnostics(Func<string>? additionalDiagnostics) =>
        DescribeDiagnostics(CopyLines(myStdOutLines), additionalDiagnostics);

    private void SendEnvelope(string type, string? role = null, object? payload = null, string? requestId = null)
    {
        var envelope = new Dictionary<string, object?> { ["version"] = ProtocolVersion, ["type"] = type };
        if (role is not null)
        {
            envelope["role"] = role;
        }
        if (requestId is not null)
        {
            envelope["requestId"] = requestId;
        }
        if (payload is not null)
        {
            envelope["payload"] = payload;
        }
        myProcess.StandardInput.WriteLine(JsonSerializer.Serialize(envelope));
        myProcess.StandardInput.Flush();
    }

    private void Drain(StreamReader reader, List<string> destination) =>
        Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (myLinesLock)
                {
                    destination.Add(line);
                }
            }
        });

    private async Task<JsonElement> WaitForMessageAsync(
        Func<JsonElement, bool> predicate, string description, TimeSpan? timeout, Func<string>? additionalDiagnostics, int skip = 0)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var stdOut = CopyLines(myStdOutLines);
            var remainingToSkip = skip;
            foreach (var line in stdOut)
            {
                using var document = JsonDocument.Parse(line);
                if (predicate(document.RootElement))
                {
                    if (remainingToSkip > 0)
                    {
                        remainingToSkip--;
                        continue;
                    }
                    return document.RootElement.Clone();
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new HeadlessUiWaitTimeoutException(description, DescribeDiagnostics(stdOut, additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until the most recently published "state.snapshot" message (not just any snapshot ever
    /// observed) satisfies the given predicate - the correct proof for state that must have been removed or
    /// changed, where an earlier snapshot recorded before the change could otherwise satisfy a naive first-match
    /// search.</summary>
    private async Task<JsonElement> WaitForLatestStateSnapshotAsync(
        Func<JsonElement, bool> predicate, string description, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var stdOut = CopyLines(myStdOutLines);
            JsonElement? latestSnapshot = null;
            foreach (var line in stdOut)
            {
                using var document = JsonDocument.Parse(line);
                if (IsStateSnapshot(document.RootElement))
                {
                    latestSnapshot = document.RootElement.Clone();
                }
            }
            if (latestSnapshot is { } snapshot && predicate(snapshot))
            {
                return snapshot;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new HeadlessUiWaitTimeoutException(description, DescribeDiagnostics(stdOut, additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    private string DescribeDiagnostics(List<string> capturedStdOut, Func<string>? additionalDiagnostics)
    {
        var core = $"""
            Process:
            {ProcessDiagnostics.Describe(myProcess)}
            Last known UI state:
            {SummarizeUiState(capturedStdOut)}
            StdOut:
            {string.Join('\n', capturedStdOut)}
            StdErr:
            {string.Join('\n', CopyLines(myStdErrLines))}
            """;
        return additionalDiagnostics is null ? core : $"{core}\n{additionalDiagnostics()}";
    }

    private List<string> CopyLines(List<string> lines)
    {
        lock (myLinesLock)
        {
            return [.. lines];
        }
    }

    private static string SummarizeUiState(IReadOnlyList<string> stdOutLines)
    {
        string? lastSnapshot = null;
        foreach (var line in stdOutLines)
        {
            using var document = JsonDocument.Parse(line);
            if (IsStateSnapshot(document.RootElement))
            {
                lastSnapshot = line;
            }
        }
        return lastSnapshot ?? "(no state.snapshot message observed)";
    }

    private static bool IsStateSnapshot(JsonElement element) => IsType(element, "state.snapshot");

    private static bool IsProtocolError(JsonElement element) => IsType(element, "protocol.error");

    private static bool IsTranscriptUpdate(JsonElement element, string role, string content)
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

    private static bool IsMatchingTranscriptEntryUpdate(JsonElement element, string role, string source, string? content)
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

    private static bool IsMatchingTranscriptOperationUpdate(JsonElement element, string role, string operation, string? content)
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

    private static string? ResolveTranscriptUpdateContent(JsonElement payload)
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

    private static TranscriptUpdateObservation ParseTranscriptUpdate(JsonElement element)
    {
        var payload = GetPayload(element);
        var role = payload.GetProperty("role").GetString()!;
        var sequence = payload.GetProperty("sequence").GetInt64();
        var operation = payload.GetProperty("operation").GetString()!;
        var entryIndex = payload.GetProperty("entryIndex").GetInt32();
        var source = payload.TryGetProperty("entry", out var entry) && entry.ValueKind == JsonValueKind.Object
            && entry.TryGetProperty("source", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.String
            ? sourceElement.GetString()
            : null;
        var content = ResolveTranscriptUpdateContent(payload);
        return new TranscriptUpdateObservation(role, sequence, operation, entryIndex, source, content);
    }

    private static bool TryGetTranscriptSynchronizationEntries(
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

    private static IReadOnlyList<TranscriptEntryObservation> ParseTranscriptEntries(JsonElement roleElement)
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

    private static long GetRoleSynchronizationSequence(JsonElement element, string role)
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

    private static bool RoleHasStatus(JsonElement element, string role, string status)
    {
        if (!GetPayload(element).TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var roleElement in roles.EnumerateArray())
        {
            if (roleElement.TryGetProperty("role", out var name) && name.GetString() == role
                && roleElement.TryGetProperty("status", out var statusElement) && statusElement.GetString() == status)
            {
                return true;
            }
        }
        return false;
    }

    private static bool RoleHasAicUsed(JsonElement element, string role, decimal aicUsed)
    {
        if (!GetPayload(element).TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var roleElement in roles.EnumerateArray())
        {
            if (roleElement.TryGetProperty("role", out var name) && name.GetString() == role
                && roleElement.TryGetProperty("aicUsed", out var aicUsedElement)
                && aicUsedElement.ValueKind == JsonValueKind.Number
                && aicUsedElement.GetDecimal() == aicUsed)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasPendingPermission(JsonElement element, string role, string requestId, string description)
    {
        if (!GetPayload(element).TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var permission in permissions.EnumerateArray())
        {
            if (MatchesRoleAndRequestId(permission, role, requestId)
                && permission.TryGetProperty("description", out var descriptionElement)
                && descriptionElement.GetString() == description)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasPendingPermissionWithId(JsonElement element, string role, string requestId)
    {
        if (!GetPayload(element).TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var permission in permissions.EnumerateArray())
        {
            if (MatchesRoleAndRequestId(permission, role, requestId))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasPendingInput(
        JsonElement element, string role, string requestId, string prompt, IReadOnlyList<string>? choices, bool allowFreeform)
    {
        if (!GetPayload(element).TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var input in inputs.EnumerateArray())
        {
            if (!MatchesRoleAndRequestId(input, role, requestId)
                || !input.TryGetProperty("prompt", out var promptElement)
                || promptElement.GetString() != prompt
                || !input.TryGetProperty("allowFreeform", out var allowFreeformElement)
                || allowFreeformElement.GetBoolean() != allowFreeform)
            {
                continue;
            }
            if (!MatchesChoices(input, choices))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    private static bool MatchesChoices(JsonElement input, IReadOnlyList<string>? choices)
    {
        var hasChoicesProperty = input.TryGetProperty("choices", out var choicesElement)
            && choicesElement.ValueKind == JsonValueKind.Array;
        if (choices is null)
        {
            return !hasChoicesProperty || choicesElement.GetArrayLength() == 0;
        }
        if (!hasChoicesProperty)
        {
            return false;
        }
        return choicesElement.EnumerateArray().Select(entry => entry.GetString()).SequenceEqual(choices);
    }

    private static bool HasPendingElicitation(
        JsonElement element, string role, string requestId, string prompt, string mode, string? url)
    {
        if (!GetPayload(element).TryGetProperty("elicitations", out var elicitations) || elicitations.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var elicitation in elicitations.EnumerateArray())
        {
            if (MatchesRoleAndRequestId(elicitation, role, requestId)
                && elicitation.TryGetProperty("prompt", out var promptElement) && promptElement.GetString() == prompt
                && elicitation.TryGetProperty("mode", out var modeElement) && modeElement.GetString() == mode
                && elicitation.TryGetProperty("url", out var urlElement)
                && (url is null ? urlElement.ValueKind == JsonValueKind.Null : urlElement.GetString() == url))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesRoleAndRequestId(JsonElement element, string role, string requestId) =>
        element.TryGetProperty("role", out var roleElement) && roleElement.GetString() == role
            && element.TryGetProperty("requestId", out var requestIdElement) && requestIdElement.GetString() == requestId;

    private static bool IsType(JsonElement element, string type) =>
        element.TryGetProperty("type", out var typeElement) && typeElement.GetString() == type;

    private static JsonElement GetPayload(JsonElement element) => element.GetProperty("payload");
}
