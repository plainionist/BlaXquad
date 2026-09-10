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
    private readonly TextWriter myStandardInput;
    private readonly object myLinesLock = new();
    private readonly List<string> myStdOutLines = [];
    private readonly List<string> myStdErrLines = [];

    /// <summary>
    /// Convenience constructor for the common case: a process launched by <see cref="System.Diagnostics.Process.Start()" />
    /// with redirected standard streams, so the streams can be read directly off the process object.
    /// </summary>
    public HeadlessUiClient(System.Diagnostics.Process process)
        : this(process, process.StandardInput, process.StandardOutput, process.StandardError)
    {
    }

    /// <summary>
    /// Constructor for processes whose standard streams were not created via <see cref="System.Diagnostics.Process.Start()" />
    /// (for example, a process launched through a platform-specific cancellation-capable launcher that wires its own
    /// pipes) - the streams are supplied explicitly instead of being read off the process object.
    /// </summary>
    public HeadlessUiClient(System.Diagnostics.Process process, TextWriter standardInput, TextReader standardOutput, TextReader standardError)
    {
        myProcess = process;
        myStandardInput = standardInput;
        Drain(standardOutput, myStdOutLines);
        Drain(standardError, myStdErrLines);
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

    /// <summary>Requests the given role's previous transcript page - the entries immediately preceding
    /// <paramref name="beforeIndex"/> - through the real "transcript.page" command, producing a new
    /// "transcript.page" message bounded to the protocol's page size, exactly as a dashboard paging back through
    /// older history relies on.</summary>
    public void RequestTranscriptPage(string role, int beforeIndex) =>
        SendEnvelope("transcript.page", role, new { beforeIndex });

    /// <summary>Requests one already-known entry's authoritative availability for the given role through the real
    /// "transcript.entry" command, producing a new "transcript.entry" message reporting whether it is still
    /// available - either live or preserved in the archive after eviction - or has rotated out of the archive
    /// entirely.</summary>
    public void RequestArchivedEntry(string role, int entryIndex) =>
        SendEnvelope("transcript.entry", role, new { entryIndex });

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given status.</summary>
    public Task WaitForRoleStatusAsync(string role, string status, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element) && RoleHasStatus(element, role, status),
            $"role '{role}' to report status '{status}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until the most recently published "state.snapshot" message (not just any snapshot ever
    /// observed) reports the given role at the given status - the correct proof that a role's status still holds
    /// after later, possibly stale, publication, rather than merely rematching the same earlier snapshot already
    /// observed right after termination.</summary>
    public Task WaitForLatestRoleStatusAsync(
        string role, string status, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForLatestStateSnapshotAsync(
            element => RoleHasStatus(element, role, status),
            $"role '{role}' to report status '{status}' in its latest published snapshot",
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

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given working state with the
    /// given context-token and AI-credit usage together - the combined shape the active-usage-refresh policy
    /// contract needs to prove usage reported while still working reaches the ui before idle, and that idle
    /// preserves the latest reported values.</summary>
    public Task WaitForRoleUsageSnapshotAsync(
        string role, bool isWorking, long contextUsedTokens, long contextLimitTokens, decimal aicUsed,
        TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element)
                && RoleHasUsageSnapshot(element, role, isWorking, contextUsedTokens, contextLimitTokens, aicUsed),
            $"role '{role}' to report {(isWorking ? "working" : "idle")} with context usage {contextUsedTokens} of " +
            $"{contextLimitTokens} and AI-credit usage {aicUsed}",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given active tool.</summary>
    public Task WaitForRoleActiveToolAsync(
        string role, string activeTool, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsStateSnapshot(element) && RoleHasActiveTool(element, role, activeTool),
            $"role '{role}' to report active tool '{activeTool}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until the most recently published "state.snapshot" message (not just any snapshot ever
    /// observed) reports the given role with no active tool - proving a tool completion genuinely cleared it,
    /// rather than merely matching an earlier snapshot recorded before any tool ever started.</summary>
    public Task WaitForNoActiveToolAsync(
        string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForLatestStateSnapshotAsync(
            element => RoleHasActiveTool(element, role, null),
            $"role '{role}' to report no active tool",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until a "transcript.update" message reports the given content for the given role.</summary>
    public Task WaitForTranscriptAsync(string role, string content, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForMessageAsync(
            element => IsTranscriptUpdate(element, role, content),
            $"a transcript update for role '{role}' with content '{content}'",
            timeout,
            additionalDiagnostics);

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.update" message reports an appended
    /// or replaced entry for the given role with the given source - and, unless null, the given content - and
    /// returns the dashboard protocol's typed <c>sequence</c>, <c>operation</c>, <c>entryIndex</c>, <c>source</c>,
    /// and <c>content</c> fields.</summary>
    public async Task<TranscriptUpdateObservation> WaitForTranscriptUpdateAsync(
        string role, string source, string? content = null, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null, int skip = 0)
    {
        var description = content is null
            ? $"a transcript update for role '{role}' with source '{source}'"
            : $"a transcript update for role '{role}' with source '{source}' and content '{content}'";
        var element = await WaitForMessageAsync(
            transcriptUpdate => IsMatchingTranscriptEntryUpdate(transcriptUpdate, role, source, content),
            description,
            timeout,
            additionalDiagnostics,
            skip);
        return ParseTranscriptUpdate(element);
    }

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.update" message reports the given
    /// operation for the given role - and, unless null, the given content - and returns the dashboard protocol's
    /// typed fields. Unlike <see cref="WaitForTranscriptUpdateAsync(string,string,string?,TimeSpan?,Func{string}?)"/>
    /// - which matches by source and therefore only ever observes an appended or replaced entry - this resolves
    /// content from either the appended/replaced entry or an "append-content" update's top-level delta fragment, so
    /// it can also observe a streamed continuation that carries no source of its own.</summary>
    public async Task<TranscriptUpdateObservation> WaitForTranscriptUpdateByOperationAsync(
        string role, string operation, string? content = null, int skip = 0, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var description = content is null
            ? $"a transcript update for role '{role}' with operation '{operation}'"
            : $"a transcript update for role '{role}' with operation '{operation}' and content '{content}'";
        var element = await WaitForMessageAsync(
            transcriptUpdate => IsMatchingTranscriptOperationUpdate(transcriptUpdate, role, operation, content),
            description,
            timeout,
            additionalDiagnostics,
            skip);
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

    /// <summary>Counts how many "transcript.synchronize" messages including an entry list for the given role have
    /// been captured so far. A caller that needs to observe a synchronization published strictly after this point -
    /// not the initial "ui.ready" handshake or any earlier explicit request that already satisfies some predicate -
    /// passes this count as <c>skip</c> to <see cref="WaitForNextTranscriptSynchronizationAsync"/>.</summary>
    public int CountTranscriptSynchronizations(string role) =>
        CopyLines(myStdOutLines).Count(line =>
        {
            using var document = JsonDocument.Parse(line);
            return TryGetTranscriptSynchronizationEntries(document.RootElement, role, out _);
        });

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.synchronize" message that includes
    /// an entry list for the given role has been published - identified purely by its structural presence, never
    /// by its content - and returns the dashboard protocol's typed <c>sequence</c> and entries for that role.
    /// Unlike <see cref="WaitForTranscriptSynchronizationAsync"/>, which searches for any message (past or future)
    /// whose entries already satisfy a predicate and so can be satisfied by an earlier synchronization observed
    /// before a just-issued request even completes, this identifies the exact synchronization a specific request
    /// produced - so a caller can then assert on its entries directly and genuinely fail if that particular
    /// response carries an unexpected value, rather than silently skipping past it to a later, correct one.</summary>
    public async Task<TranscriptSynchronizationObservation> WaitForNextTranscriptSynchronizationAsync(
        string role, int skip, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var element = await WaitForMessageAsync(
            candidate => TryGetTranscriptSynchronizationEntries(candidate, role, out _),
            $"a new transcript synchronization for role '{role}'",
            timeout,
            additionalDiagnostics,
            skip);
        TryGetTranscriptSynchronizationEntries(element, role, out var entries);
        return new TranscriptSynchronizationObservation(role, GetRoleSynchronizationSequence(element, role), entries);
    }

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.page" message for the given role has
    /// been published (the wire payload carries no correlation id back to its triggering request, so distinguishing
    /// a specific page reply among several for the same role requires counting prior ones already observed - the
    /// same approach <see cref="WaitForProtocolErrorAsync"/> uses), and returns the dashboard protocol's typed
    /// ordered entries, "hasMore", and "historyTruncated" fields.</summary>
    public async Task<TranscriptPageObservation> WaitForTranscriptPageAsync(
        string role, int skip = 0, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var element = await WaitForMessageAsync(
            page => IsTranscriptPageForRole(page, role),
            $"a transcript page for role '{role}'",
            timeout,
            additionalDiagnostics,
            skip);
        var payload = GetPayload(element);
        return new TranscriptPageObservation(
            role,
            ParseTranscriptEntries(payload),
            payload.TryGetProperty("hasMore", out var hasMore) && hasMore.GetBoolean(),
            payload.TryGetProperty("historyTruncated", out var historyTruncated) && historyTruncated.GetBoolean());
    }

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.entry" message for the given role
    /// and entry index has been published (the wire payload carries no correlation id back to its triggering
    /// request, so distinguishing a specific reply among several requires counting prior ones already observed -
    /// the same approach <see cref="WaitForProtocolErrorAsync"/> uses), and returns the dashboard protocol's typed
    /// "sequence", "content" (null when unavailable), "contentTruncated", "totalContentCharacters", and
    /// "archivedPrefixCharacters" fields.</summary>
    public async Task<ArchivedTranscriptEntryObservation> WaitForArchivedEntryAsync(
        string role, int entryIndex, int skip = 0, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var element = await WaitForMessageAsync(
            entry => IsArchivedEntryForRoleAndIndex(entry, role, entryIndex),
            $"an archived transcript entry {entryIndex} for role '{role}'",
            timeout,
            additionalDiagnostics,
            skip);
        var payload = GetPayload(element);
        var entryElement = payload.TryGetProperty("entry", out var entryProperty) && entryProperty.ValueKind == JsonValueKind.Object
            ? entryProperty
            : (JsonElement?)null;
        return new ArchivedTranscriptEntryObservation(
            role,
            payload.TryGetProperty("sequence", out var sequence) ? sequence.GetInt64() : 0,
            entryIndex,
            entryElement?.TryGetProperty("content", out var content) == true ? content.GetString() : null,
            payload.TryGetProperty("contentTruncated", out var contentTruncated) && contentTruncated.GetBoolean(),
            payload.TryGetProperty("totalContentCharacters", out var totalContentCharacters) ? totalContentCharacters.GetInt64() : 0,
            payload.TryGetProperty("archivedPrefixCharacters", out var archivedPrefixCharacters) ? archivedPrefixCharacters.GetInt64() : 0);
    }

    /// <summary>Reconciles the most recently published transcript synchronization for the role - whichever
    /// request produced it, whether the initial "ui.ready" handshake or a later explicit request that may have
    /// raced ongoing publication - exactly as a reconnecting dashboard client must: its entries seed those already
    /// known at its reported sequence (its "high-water mark"), and every subsequent "transcript.update" message for
    /// the role whose own sequence is greater than that high-water mark is then replayed on top of it in
    /// publication order - an "append" adds its entry at its reported index, an "append-content" appends its delta
    /// fragment onto the entry already at its index, and a "replace" overwrites the entry at its index outright.
    /// Because the synchronization and any updates racing it are combined by comparing sequence numbers rather
    /// than by requiring silence or a paused publication, this proves a real client reconstructs the ordered
    /// transcript without missing or duplicated entries even when synchronization overlaps ongoing or concurrent
    /// publication. Retries until the reconciled entries satisfy the given predicate or the timeout elapses. Does
    /// not itself request a synchronization - callers that need one to race publication request it explicitly
    /// (for example through <see cref="RequestTranscriptSynchronization"/>) before or during publication.</summary>
    public async Task<IReadOnlyList<TranscriptEntryObservation>> WaitForReconciledTranscriptAsync(
        string role,
        Func<IReadOnlyList<TranscriptEntryObservation>, bool> matches,
        TimeSpan? timeout = null,
        Func<string>? additionalDiagnostics = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var stdOut = CopyLines(myStdOutLines);
            if (TryReconcileTranscript(stdOut, role, out var reconciled) && matches(reconciled))
            {
                return reconciled;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new HeadlessUiWaitTimeoutException(
                    $"a reconciled transcript synchronization for role '{role}' matching the expected entries",
                    DescribeDiagnostics(stdOut, additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
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
    /// Sends one already-serialized line verbatim, exactly as written - never through <see cref="SendEnvelope"/>'s
    /// semantic envelope construction. Exists solely for protocol-validation specifications proving the exact
    /// "protocol.error" contract for an envelope shape no semantic command method could produce: an unsupported
    /// version, a missing or unknown type, a missing role or request id, a mistyped payload field, or JSON that
    /// does not parse at all.
    /// </summary>
    public void SendRawEnvelope(string rawJsonLine)
    {
        myStandardInput.WriteLine(rawJsonLine);
        myStandardInput.Flush();
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

    /// <summary>
    /// A snapshot of every standard-error line captured from the launched process so far, joined with newlines.
    /// Exposed for specifications proving a clean CLI diagnostic reached standard error on a startup failure -
    /// distinct from <see cref="DescribeDiagnostics()"/>, which exists only for test-failure reporting.
    /// </summary>
    public string CapturedStandardError() => string.Join('\n', CopyLines(myStdErrLines));

    /// <summary>
    /// A snapshot of every standard-output line captured from the launched process so far, joined with newlines.
    /// Exposed for specifications proving nothing (or something) has reached standard output yet - for example
    /// that no protocol message is written before the "ui.ready" handshake completes.
    /// </summary>
    public string CapturedStandardOutput() => string.Join('\n', CopyLines(myStdOutLines));

    /// <summary>
    /// True only if every line captured on standard output so far is one complete, well-formed protocol envelope -
    /// a JSON object carrying the protocol's "version" and "type" fields - proving concurrent multi-role activity
    /// never tears or interleaves a line's framing, and that standard output carries nothing but the versioned
    /// protocol. False (never an exception) if no line has been captured yet, so a caller waits for genuine
    /// output before asserting on it instead of vacuously passing against an empty buffer.
    /// </summary>
    public bool EveryCapturedStandardOutputLineIsAWellFormedEnvelope() =>
        CopyLines(myStdOutLines) is { Count: > 0 } lines && lines.All(IsWellFormedEnvelope);

    /// <summary>
    /// True only if standard error carries no line that looks like a protocol envelope - proving the protocol and
    /// process-diagnostic output streams stay genuinely separate even while the protocol is active.
    /// </summary>
    public bool StandardErrorContainsNoProtocolEnvelope() => !CopyLines(myStdErrLines).Any(IsWellFormedEnvelope);

    private static bool IsWellFormedEnvelope(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out _)
                && document.RootElement.TryGetProperty("type", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
        myStandardInput.WriteLine(JsonSerializer.Serialize(envelope));
        myStandardInput.Flush();
    }

    /// <summary>
    /// Closes the process's standard input, the same observable event as a real UI process exiting or its window
    /// closing - squad-hq treats end of standard input as the "the UI is gone" signal regardless of which launcher
    /// created the process.
    /// </summary>
    public void CloseStandardInput() => myStandardInput.Close();

    private void Drain(TextReader reader, List<string> destination) =>
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
        var stdOutText = capturedStdOut.Count switch
        {
            0 => "(none)",
            <= 10 => string.Join('\n', capturedStdOut),
            _ => $"... ({capturedStdOut.Count - 10} earlier lines omitted)\n" + string.Join('\n', capturedStdOut.TakeLast(10)),
        };
        var stdErrLines = CopyLines(myStdErrLines);
        var stdErrText = stdErrLines.Count switch
        {
            0 => "(none)",
            <= 10 => string.Join('\n', stdErrLines),
            _ => $"... ({stdErrLines.Count - 10} earlier lines omitted)\n" + string.Join('\n', stdErrLines.TakeLast(10)),
        };
        var core = $"""
            Process:
            {ProcessDiagnostics.Describe(myProcess)}
            Last known UI state:
            {SummarizeUiState(capturedStdOut)}
            StdOut:
            {stdOutText}
            StdErr:
            {stdErrText}
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

    private static bool IsTranscriptPageForRole(JsonElement element, string role)
    {
        if (!IsType(element, "transcript.page"))
        {
            return false;
        }
        var payload = GetPayload(element);
        return payload.TryGetProperty("role", out var roleElement) && roleElement.GetString() == role;
    }

    private static bool IsArchivedEntryForRoleAndIndex(JsonElement element, string role, int entryIndex)
    {
        if (!IsType(element, "transcript.entry"))
        {
            return false;
        }
        var payload = GetPayload(element);
        return payload.TryGetProperty("role", out var roleElement) && roleElement.GetString() == role
            && payload.TryGetProperty("entryIndex", out var entryIndexElement) && entryIndexElement.GetInt32() == entryIndex;
    }

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

    /// <summary>Reconciles the most recently published "transcript.synchronize" message for the role (its
    /// entries seed the result, its sequence is the high-water mark) with every "transcript.update" message for
    /// the role published afterward, applied in publication order by "append"/"append-content"/"replace"
    /// semantics. Returns false only if no synchronization for the role has been published yet.</summary>
    private static bool TryReconcileTranscript(
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

    /// <summary>Matches a role's published "isWorking", "contextUsedTokens", "contextLimitTokens", and "aicUsed"
    /// fields together, so a caller can prove the exact combination reported while still working or after idle,
    /// rather than checking each field in isolation against a possibly different snapshot.</summary>
    private static bool RoleHasUsageSnapshot(
        JsonElement element, string role, bool isWorking, long contextUsedTokens, long contextLimitTokens, decimal aicUsed)
    {
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
            if (!roleElement.TryGetProperty("isWorking", out var isWorkingElement)
                || isWorkingElement.ValueKind != JsonValueKind.True && isWorkingElement.ValueKind != JsonValueKind.False
                || isWorkingElement.GetBoolean() != isWorking)
            {
                return false;
            }
            if (!roleElement.TryGetProperty("contextUsedTokens", out var used)
                || used.ValueKind != JsonValueKind.Number || used.GetInt64() != contextUsedTokens)
            {
                return false;
            }
            if (!roleElement.TryGetProperty("contextLimitTokens", out var limit)
                || limit.ValueKind != JsonValueKind.Number || limit.GetInt64() != contextLimitTokens)
            {
                return false;
            }
            if (!roleElement.TryGetProperty("aicUsed", out var aic)
                || aic.ValueKind != JsonValueKind.Number || aic.GetDecimal() != aicUsed)
            {
                return false;
            }
            return true;
        }
        return false;
    }

    /// <summary>Matches a role's published "activeTool" field, either against a specific expected tool name or -
    /// when <paramref name="activeTool"/> is null - against no active tool at all.</summary>
    private static bool RoleHasActiveTool(JsonElement element, string role, string? activeTool)
    {
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
            var observedActiveTool = roleElement.TryGetProperty("activeTool", out var activeToolElement)
                && activeToolElement.ValueKind == JsonValueKind.String
                ? activeToolElement.GetString()
                : null;
            return observedActiveTool == activeTool;
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
