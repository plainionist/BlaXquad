using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>
/// Test-runner end of the private fake-provider control transport: a uniquely named local named pipe whose name
/// and random per-scenario authentication token travel to the launched squad-hq process only through the
/// <see cref="PipeNameEnvironmentVariable"/> and <see cref="TokenEnvironmentVariable"/> environment variables -
/// squad-hq itself never parses, forwards, or otherwise knows about this channel. Every exchange is one typed,
/// versioned, newline-delimited JSON envelope carrying a correlation id; the shared <see cref="ControlPipeDuplex"/>
/// gives this endpoint exactly one background dispatch loop, so concurrent session observations and their
/// acknowledgements can never compete to read the pipe.
/// </summary>
public sealed class FakeProviderControlServer : IAsyncDisposable
{
    public const string PipeNameEnvironmentVariable = "BLAXQUAD_FAKE_CONTROL_PIPE";
    public const string TokenEnvironmentVariable = "BLAXQUAD_FAKE_CONTROL_TOKEN";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly NamedPipeServerStream myPipe;
    private readonly object myStateLock = new();
    private readonly List<(string Role, string Type, string SessionId)> myObservations = [];
    private readonly List<string> myProtocolErrors = [];
    private readonly Dictionary<string, string> myActiveSessionByRole = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> myLatestPromptByRole = new(StringComparer.Ordinal);
    private ControlPipeDuplex? myDuplex;
    private bool myAuthenticated;

    private FakeProviderControlServer(string pipeName, string token)
    {
        PipeName = pipeName;
        Token = token;
        myPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    /// <summary>The unique per-scenario pipe name, published to the launched process only via
    /// <see cref="PipeNameEnvironmentVariable"/>.</summary>
    public string PipeName { get; }

    /// <summary>The random per-scenario authentication token every connecting client must present on
    /// "connect", published to the launched process only via <see cref="TokenEnvironmentVariable"/>.</summary>
    public string Token { get; }

    /// <summary>Creates one server bound to a freshly generated, globally unique pipe name and a random token.</summary>
    public static FakeProviderControlServer Create() =>
        new($"blaxquad-fake-control-{Guid.NewGuid():N}", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    /// <summary>Waits for the provider-process client to connect and starts the one background dispatch loop
    /// that owns every subsequent read from the pipe.</summary>
    public async Task WaitForConnectionAsync(TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout ?? DefaultTimeout);
        try
        {
            await myPipe.WaitForConnectionAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new FakeProviderControlTimeoutException(
                "a client to connect to the fake-provider control pipe", DescribeDiagnostics(additionalDiagnostics));
        }
        myDuplex = new ControlPipeDuplex(myPipe, HandleUnsolicitedAsync);
        myDuplex.StartDispatching();
    }

    /// <summary>Waits until the connected client has reported (and this server has acknowledged) that the given
    /// role's session started.</summary>
    public Task WaitForSessionStartedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForObservationAsync(role, "session-started", timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported (and this server has acknowledged) that the given
    /// role's session was disposed.</summary>
    public Task WaitForSessionDisposedAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null) =>
        WaitForObservationAsync(role, "session-disposed", timeout, additionalDiagnostics);

    /// <summary>Waits until the connected client has reported a prompt sent to the given role, and returns its
    /// content.</summary>
    public async Task<string> WaitForPromptAsync(string role, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (myStateLock)
            {
                if (myLatestPromptByRole.TryGetValue(role, out var prompt))
                {
                    return prompt;
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report a prompt across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>
    /// Sends a semantic assistant reply for the given role's session across the pipe and awaits the client's
    /// acknowledgement - or throws a <see cref="FakeProviderControlProtocolException"/> immediately if no session
    /// has ever been observed for that role, or with the client's own explicit diagnostic if the client rejects
    /// the reply (for example because the session has since been disposed), or a
    /// <see cref="FakeProviderControlTimeoutException"/> carrying this server's combined diagnostics if the client
    /// never acknowledges within the given timeout.
    /// </summary>
    public async Task ReplyAsync(string role, string content, TimeSpan? timeout = null, Func<string>? additionalDiagnostics = null)
    {
        string sessionId;
        lock (myStateLock)
        {
            if (!myActiveSessionByRole.TryGetValue(role, out sessionId!))
            {
                throw new FakeProviderControlProtocolException(
                    $"No fake-provider session has been observed for role '{role}'.");
            }
        }

        JsonElement response;
        try
        {
            response = await myDuplex!.SendAndAwaitAsync(
                "reply", new { role, sessionId, content }, timeout ?? DefaultTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            throw new FakeProviderControlTimeoutException(
                $"the client to acknowledge a reply for role '{role}'", DescribeDiagnostics(additionalDiagnostics));
        }
        ControlPipeDuplex.EnsureNotProtocolError(response, "reply");
    }

    private async Task WaitForObservationAsync(string role, string type, TimeSpan? timeout, Func<string>? additionalDiagnostics)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (myStateLock)
            {
                if (myObservations.Any(observation => observation.Role == role && observation.Type == type))
                {
                    return;
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new FakeProviderControlTimeoutException(
                    $"role '{role}' to report '{type}' across the fake-provider control pipe",
                    DescribeDiagnostics(additionalDiagnostics));
            }
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>
    /// Builds a diagnostics snapshot of every session-lifecycle observation, latest prompt per role, and
    /// protocol error this server has seen, so a caller beyond this server's own semantic waits (for example
    /// <see cref="BackendScenario"/> combining this with process and UI protocol diagnostics) can report the
    /// same bounded-wait diagnostics without inspecting raw control-pipe traffic by hand.
    /// </summary>
    public string DescribeDiagnostics() => DescribeDiagnostics(additionalDiagnostics: null);

    private string DescribeDiagnostics(Func<string>? additionalDiagnostics)
    {
        lock (myStateLock)
        {
            var observations = myObservations.Count == 0
                ? "(none)"
                : string.Join('\n', myObservations.Select(observation =>
                    $"{observation.Type} role='{observation.Role}' session='{observation.SessionId}'"));
            var prompts = myLatestPromptByRole.Count == 0
                ? "(none)"
                : string.Join('\n', myLatestPromptByRole.Select(entry => $"role='{entry.Key}' prompt='{entry.Value}'"));
            var protocolErrors = myProtocolErrors.Count == 0 ? "(none)" : string.Join('\n', myProtocolErrors);
            var provider = $"""
                Observations:
                {observations}
                Latest prompts:
                {prompts}
                Protocol errors:
                {protocolErrors}
                """;
            return additionalDiagnostics is null ? provider : $"{provider}\n{additionalDiagnostics()}";
        }
    }

    private async Task HandleUnsolicitedAsync(JsonElement envelope, CancellationToken cancellationToken)
    {
        var correlationId = envelope.TryGetProperty("correlationId", out var correlationElement)
            && correlationElement.ValueKind == JsonValueKind.String
            ? correlationElement.GetString()
            : null;

        if (string.IsNullOrEmpty(correlationId))
        {
            await ReplyProtocolErrorAsync(correlationId ?? "", "Missing or empty correlation id.", cancellationToken);
            return;
        }

        if (!envelope.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || versionElement.GetInt32() != ControlPipeDuplex.ProtocolVersion)
        {
            await ReplyProtocolErrorAsync(
                correlationId, $"Unsupported protocol version. Expected {ControlPipeDuplex.ProtocolVersion}.", cancellationToken);
            return;
        }

        var type = envelope.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var payload = envelope.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;

        switch (type)
        {
            case "connect":
                await HandleConnectAsync(correlationId, payload, cancellationToken);
                break;
            case "session-started":
            case "session-disposed":
                await HandleObservationAsync(type, correlationId, payload, cancellationToken);
                break;
            case "prompt":
                await HandlePromptAsync(correlationId, payload, cancellationToken);
                break;
            default:
                await ReplyProtocolErrorAsync(correlationId, $"Unknown command '{type}'.", cancellationToken);
                break;
        }
    }

    private async Task HandleConnectAsync(string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        var token = payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("token", out var tokenElement)
            && tokenElement.ValueKind == JsonValueKind.String
            ? tokenElement.GetString()
            : null;
        if (!string.Equals(token, Token, StringComparison.Ordinal))
        {
            await ReplyProtocolErrorAsync(correlationId, "Invalid authentication token.", cancellationToken);
            return;
        }

        lock (myStateLock)
        {
            myAuthenticated = true;
        }
        await myDuplex!.SendAsync("connected", correlationId, null, cancellationToken);
    }

    private async Task HandleObservationAsync(string type, string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        bool authenticated;
        lock (myStateLock)
        {
            authenticated = myAuthenticated;
        }
        if (!authenticated)
        {
            await ReplyProtocolErrorAsync(correlationId, "The connection has not authenticated.", cancellationToken);
            return;
        }

        var role = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString()
            : null;
        var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(sessionId))
        {
            await ReplyProtocolErrorAsync(correlationId, $"'{type}' requires a role and a session id.", cancellationToken);
            return;
        }

        lock (myStateLock)
        {
            myObservations.Add((role, type, sessionId));
            if (type == "session-started")
            {
                myActiveSessionByRole[role] = sessionId;
            }
        }
        await myDuplex!.SendAsync("ack", correlationId, new { type }, cancellationToken);
    }

    private async Task HandlePromptAsync(string correlationId, JsonElement payload, CancellationToken cancellationToken)
    {
        bool authenticated;
        lock (myStateLock)
        {
            authenticated = myAuthenticated;
        }
        if (!authenticated)
        {
            await ReplyProtocolErrorAsync(correlationId, "The connection has not authenticated.", cancellationToken);
            return;
        }

        var role = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString()
            : null;
        var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        var prompt = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("prompt", out var promptElement)
            ? promptElement.GetString()
            : null;
        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(sessionId) || prompt is null)
        {
            await ReplyProtocolErrorAsync(correlationId, "'prompt' requires a role, a session id, and prompt text.", cancellationToken);
            return;
        }

        lock (myStateLock)
        {
            myLatestPromptByRole[role] = prompt;
        }
        await myDuplex!.SendAsync("ack", correlationId, new { type = "prompt" }, cancellationToken);
    }

    private async Task ReplyProtocolErrorAsync(string correlationId, string message, CancellationToken cancellationToken)
    {
        lock (myStateLock)
        {
            myProtocolErrors.Add(message);
        }
        await myDuplex!.SendAsync("protocol-error", correlationId, new { message }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (myDuplex is not null)
        {
            await myDuplex.DisposeAsync();
        }
        else
        {
            await myPipe.DisposeAsync();
        }
    }
}
