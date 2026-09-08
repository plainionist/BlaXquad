using System.IO.Pipes;
using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>
/// Provider-process end of <see cref="FakeProviderControlServer"/>, loaded into the real squad-hq process by
/// <see cref="FakeAgentProviderFactory"/>. Connects to the pipe named by
/// <see cref="FakeProviderControlServer.PipeNameEnvironmentVariable"/>, authenticates with the token named by
/// <see cref="FakeProviderControlServer.TokenEnvironmentVariable"/>, and reports every session start and disposal
/// across the pipe so a test-runner scenario can observe the fake provider's real lifecycle without any product
/// test hook. Every send awaits its correlated acknowledgement or explicit protocol error through the shared
/// <see cref="ControlPipeDuplex"/>'s one dispatch loop, so concurrent notifications from multiple sessions never
/// compete to read the pipe themselves.
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
    public static async Task<FakeProviderControlClient?> ConnectIfConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var pipeName = Environment.GetEnvironmentVariable(FakeProviderControlServer.PipeNameEnvironmentVariable);
        var token = Environment.GetEnvironmentVariable(FakeProviderControlServer.TokenEnvironmentVariable);
        if (string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(token))
        {
            return null;
        }

        return await ConnectAsync(pipeName, token, cancellationToken);
    }

    /// <summary>Connects to the given pipe and authenticates with the given token directly - no environment
    /// variable involved - for in-process specifications of the control transport itself.</summary>
    public static async Task<FakeProviderControlClient> ConnectAsync(string pipeName, string token, CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);
        var duplex = new ControlPipeDuplex(
            pipe,
            (_, _) => throw new InvalidOperationException("The fake-provider control client never receives unsolicited messages."));
        duplex.StartDispatching();

        var response = await duplex.SendAndAwaitAsync("connect", new { token }, DefaultTimeout, cancellationToken);
        EnsureNotProtocolError(response, "connect");
        return new FakeProviderControlClient(duplex);
    }

    /// <summary>Reports that the given role's session started and awaits the server's acknowledgement.</summary>
    public Task NotifySessionStartedAsync(string role, string sessionId, CancellationToken cancellationToken = default) =>
        NotifyAsync("session-started", role, sessionId, cancellationToken);

    /// <summary>Reports that the given role's session was disposed and awaits the server's acknowledgement.</summary>
    public Task NotifySessionDisposedAsync(string role, string sessionId, CancellationToken cancellationToken = default) =>
        NotifyAsync("session-disposed", role, sessionId, cancellationToken);

    private async Task NotifyAsync(string type, string role, string sessionId, CancellationToken cancellationToken)
    {
        var response = await myDuplex.SendAndAwaitAsync(type, new { role, sessionId }, DefaultTimeout, cancellationToken);
        EnsureNotProtocolError(response, type);
    }

    private static void EnsureNotProtocolError(JsonElement response, string requestType)
    {
        if (!response.TryGetProperty("type", out var typeElement) || typeElement.GetString() != "protocol-error")
        {
            return;
        }
        var message = response.TryGetProperty("payload", out var payload)
            && payload.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : "(no message)";
        throw new FakeProviderControlProtocolException($"The fake-provider control pipe rejected '{requestType}': {message}");
    }

    public ValueTask DisposeAsync() => myDuplex.DisposeAsync();
}
