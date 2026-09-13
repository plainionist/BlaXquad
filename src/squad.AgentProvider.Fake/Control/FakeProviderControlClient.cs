using System.IO.Pipes;
using System.Text.Json;

namespace squad.AgentProvider.Fake.Control;

/// <summary>
/// Provider-process end of <see cref="FakeProviderControlServer"/>, loaded into the real squad-hq process by
/// <see cref="FakeAgentProviderFactory"/>. Connects to the pipe named by
/// <see cref="FakeProviderControlServer.PipeNameEnvironmentVariable"/>, authenticates with the token named by
/// <see cref="FakeProviderControlServer.TokenEnvironmentVariable"/>, and reports every session start, prompt,
/// generic observation (harness message, abort, or interaction response), and disposal across the pipe so a
/// test-runner scenario can observe the fake provider's real lifecycle without any product test hook. It also
/// accepts "reply" and "emit" commands pushed by the server, routing each to the given
/// <see cref="FakeProviderReplyHandler"/> or <see cref="FakeProviderEmitHandler"/> and replying with an
/// acknowledgement or an explicit protocol error. Every send awaits its correlated acknowledgement or explicit
/// protocol error through the shared <see cref="ControlPipeDuplex"/>'s one dispatch loop, so concurrent
/// notifications from multiple sessions, and inbound commands, never compete to read the pipe themselves.
/// </summary>
internal sealed class FakeProviderControlClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ControlPipeDuplex myDuplex;

    private FakeProviderControlClient(ControlPipeDuplex duplex)
    {
        myDuplex = duplex;
    }

    /// <summary>
    /// Reads the pipe name and token from the environment, connects, and authenticates - or returns null if the
    /// environment does not name a control pipe, so the fake provider keeps working exactly as it did before this
    /// slice when no scenario has enabled the control transport. Only the launched squad-hq process itself should
    /// ever read these variables from its own environment: use <see cref="ConnectAsync"/> directly for in-process
    /// specifications, which must never set these variables on the shared test process.
    /// </summary>
    public static async Task<FakeProviderControlClient?> ConnectIfConfiguredAsync(
        FakeProviderReplyHandler onReply, FakeProviderEmitHandler onEmit, FakeProviderFailBackendHandler onFailBackend,
        CancellationToken cancellationToken = default)
    {
        var pipeName = Environment.GetEnvironmentVariable(FakeProviderControlServer.PipeNameEnvironmentVariable);
        var token = Environment.GetEnvironmentVariable(FakeProviderControlServer.TokenEnvironmentVariable);

        if (string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(token))
        {
            return null;
        }

        return await ConnectAsync(pipeName, token, onReply, onEmit, onFailBackend, cancellationToken);
    }

    /// <summary>Connects to the given pipe and authenticates with the given token directly - no environment
    /// variable involved - for in-process specifications of the control transport itself.</summary>
    public static async Task<FakeProviderControlClient> ConnectAsync(
        string pipeName, string token, FakeProviderReplyHandler onReply, FakeProviderEmitHandler onEmit,
        FakeProviderFailBackendHandler onFailBackend, CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);

        ControlPipeDuplex? duplex = null;
        duplex = new ControlPipeDuplex(
            pipe, (envelope, ct) => HandleUnsolicitedAsync(envelope, duplex!, onReply, onEmit, onFailBackend, ct));
        duplex.StartDispatching();

        var response = await duplex.SendAndAwaitAsync("connect", new { token }, DefaultTimeout, cancellationToken);
        ControlPipeDuplex.EnsureNotProtocolError(response, "connect");
        return new FakeProviderControlClient(duplex);
    }

    /// <summary>Reports that the given role's session started and awaits the server's acknowledgement.</summary>
    public Task NotifySessionStartedAsync(string role, string sessionId, CancellationToken cancellationToken = default) =>
        NotifyAsync("session-started", new { role, sessionId }, cancellationToken);

    /// <summary>Reports that the given role's session was disposed and awaits the server's acknowledgement.</summary>
    public Task NotifySessionDisposedAsync(string role, string sessionId, CancellationToken cancellationToken = default) =>
        NotifyAsync("session-disposed", new { role, sessionId }, cancellationToken);

    /// <summary>Reports a prompt the session received and awaits the server's acknowledgement.</summary>
    public Task NotifyPromptAsync(string role, string sessionId, string prompt, CancellationToken cancellationToken = default) =>
        NotifyAsync("prompt", new { role, sessionId, prompt }, cancellationToken);

    /// <summary>
    /// Reports one generic observation of the given kind (for example a harness message, an abort, or an
    /// interaction response) that this role's session received, carrying whatever kind-specific fields the data
    /// object holds, and awaits the server's acknowledgement.
    /// </summary>
    public Task NotifyObservationAsync(
        string role, string sessionId, string kind, object data, CancellationToken cancellationToken = default) =>
        NotifyAsync("observe", new { role, sessionId, kind, data }, cancellationToken);

    private async Task NotifyAsync(string type, object payload, CancellationToken cancellationToken)
    {
        var response = await myDuplex.SendAndAwaitAsync(type, payload, DefaultTimeout, cancellationToken);
        ControlPipeDuplex.EnsureNotProtocolError(response, type);
    }

    /// <summary>Handles every message this client did not itself request: the server's "reply" and "emit"
    /// pushes, validated the same way <see cref="FakeProviderControlServer"/> validates inbound requests.</summary>
    private static async Task HandleUnsolicitedAsync(
        JsonElement envelope, ControlPipeDuplex duplex, FakeProviderReplyHandler onReply, FakeProviderEmitHandler onEmit,
        FakeProviderFailBackendHandler onFailBackend, CancellationToken cancellationToken)
    {
        var correlationId = envelope.TryGetProperty("correlationId", out var correlationElement)
            && correlationElement.ValueKind == JsonValueKind.String
            ? correlationElement.GetString()
            : null;

        if (string.IsNullOrEmpty(correlationId))
        {
            // No correlation id to reply under - nothing this client sends back could ever be routed to a
            // waiter, so there is nothing actionable to do beyond dropping the malformed message.
            return;
        }

        if (!envelope.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || versionElement.GetInt32() != ControlPipeDuplex.ProtocolVersion)
        {

            await duplex.SendAsync(
                "protocol-error", correlationId,
                new { message = $"Unsupported protocol version. Expected {ControlPipeDuplex.ProtocolVersion}." },
                cancellationToken);
            return;
        }

        var type = envelope.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var payload = envelope.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;

        switch (type)
        {
            case "reply":
                await HandleReplyAsync(duplex, correlationId, payload, onReply, cancellationToken);
                break;
            case "emit":
                await HandleEmitAsync(duplex, correlationId, payload, onEmit, cancellationToken);
                break;
            case "fail-backend":
                await HandleFailBackendAsync(duplex, correlationId, payload, onFailBackend, cancellationToken);
                break;
            default:
                await duplex.SendAsync("protocol-error", correlationId, new { message = $"Unknown command '{type}'." }, cancellationToken);
                break;
        }
    }

    private static async Task HandleReplyAsync(
        ControlPipeDuplex duplex, string correlationId, JsonElement payload, FakeProviderReplyHandler onReply,
        CancellationToken cancellationToken)
    {
        var role = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString()
            : null;
        var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        var content = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString()
            : null;

        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(sessionId) || content is null)
        {
            await duplex.SendAsync(
                "protocol-error", correlationId, new { message = "'reply' requires a role, a session id, and content." }, cancellationToken);
            return;
        }

        var error = await onReply(role, sessionId, content, cancellationToken);

        if (error is not null)
        {
            await duplex.SendAsync("protocol-error", correlationId, new { message = error }, cancellationToken);
            return;
        }

        await duplex.SendAsync("ack", correlationId, new { type = "reply" }, cancellationToken);
    }

    private static async Task HandleEmitAsync(
        ControlPipeDuplex duplex, string correlationId, JsonElement payload, FakeProviderEmitHandler onEmit,
        CancellationToken cancellationToken)
    {
        var role = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("role", out var roleElement)
            ? roleElement.GetString()
            : null;
        var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        var kind = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("kind", out var kindElement)
            ? kindElement.GetString()
            : null;
        var data = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("data", out var dataElement)
            ? dataElement
            : default;

        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(kind))
        {
            await duplex.SendAsync(
                "protocol-error", correlationId, new { message = "'emit' requires a role, a session id, and a kind." }, cancellationToken);
            return;
        }

        var error = await onEmit(role, sessionId, kind, data, cancellationToken);

        if (error is not null)
        {
            await duplex.SendAsync("protocol-error", correlationId, new { message = error }, cancellationToken);
            return;
        }

        await duplex.SendAsync("ack", correlationId, new { type = "emit" }, cancellationToken);
    }

    private static async Task HandleFailBackendAsync(
        ControlPipeDuplex duplex, string correlationId, JsonElement payload, FakeProviderFailBackendHandler onFailBackend,
        CancellationToken cancellationToken)
    {
        var message = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;

        if (string.IsNullOrEmpty(message))
        {
            await duplex.SendAsync(
                "protocol-error", correlationId, new { message = "'fail-backend' requires a message." }, cancellationToken);
            return;
        }

        // Acknowledges before applying the failure, unlike "reply"/"emit": applying a backend-wide failure tears
        // down the very runtime that owns this control connection (disposing its session and its control client),
        // so the ack must already be safely written to the pipe before that teardown can race ahead of it and
        // close the connection out from under an in-flight send.
        await duplex.SendAsync("ack", correlationId, new { type = "fail-backend" }, cancellationToken);
        await onFailBackend(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => myDuplex.DisposeAsync();
}
