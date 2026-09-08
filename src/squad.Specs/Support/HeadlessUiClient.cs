using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>
/// Semantic client for one launched "squad-hq --ui stdio" process. It owns the process's standard input, drains
/// standard output and standard error concurrently with two independent background readers so a full stderr pipe
/// can never block a pending stdout read (or vice versa), and privately frames the newline-delimited, versioned UI
/// protocol envelopes. Step definitions see only readiness, prompt sending, abort/interaction-response sending,
/// role-status/usage waiting, transcript waiting, protocol-error reporting, process completion, and deliberate
/// termination - never raw JSON, envelopes, streams, or the child process itself.
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

    /// <summary>
    /// Deliberately terminates the underlying process and its tree, simulating an abrupt crash rather than a
    /// normal `squad-hq shutdown`, so specifications can exercise stale-ownership recovery without ever
    /// referencing the process itself.
    /// </summary>
    public void Terminate() => myProcess.Kill(entireProcessTree: true);

    /// <summary>Waits up to the given timeout for the underlying process to exit, without exposing the process.</summary>
    public bool WaitForExit(TimeSpan timeout) => myProcess.WaitForExit((int)timeout.TotalMilliseconds);

    /// <summary>Aborts the given role's current operation through the real "role.abort" command.</summary>
    public void SendAbort(string role) => SendEnvelope("role.abort", role);

    /// <summary>Responds to a permission request through the real "permission.respond" command.</summary>
    public void RespondToPermission(string role, string requestId, bool approved) =>
        SendEnvelope("permission.respond", role, new { approved }, requestId);

    /// <summary>Responds to an input request through the real "input.respond" command.</summary>
    public void RespondToInput(string role, string requestId, string? answer, bool wasFreeform) =>
        SendEnvelope("input.respond", role, new { answer, wasFreeform }, requestId);

    /// <summary>Responds to an elicitation request through the real "elicitation.respond" command.</summary>
    public void RespondToElicitation(string role, string requestId, string action) =>
        SendEnvelope("elicitation.respond", role, new { action }, requestId);

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

    /// <summary>Waits for a "protocol.error" message and returns its human-readable message.</summary>
    public async Task<string> WaitForProtocolErrorAsync(TimeSpan? timeout = null)
    {
        var element = await WaitForMessageAsync(IsProtocolError, "a protocol.error message", timeout, additionalDiagnostics: null);
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
        Func<JsonElement, bool> predicate, string description, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var stdOut = CopyLines(myStdOutLines);
            foreach (var line in stdOut)
            {
                using var document = JsonDocument.Parse(line);
                if (predicate(document.RootElement))
                {
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

    private static bool IsType(JsonElement element, string type) =>
        element.TryGetProperty("type", out var typeElement) && typeElement.GetString() == type;

    private static JsonElement GetPayload(JsonElement element) => element.GetProperty("payload");
}
