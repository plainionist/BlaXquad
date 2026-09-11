using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace squad.AgentProvider.Fake.Control;

/// <summary>
/// Shared newline-delimited, versioned JSON duplex for one end of the fake-provider control pipe. Owns exactly
/// one background dispatch loop reading the underlying pipe stream, so every reply correlated by id and every
/// unsolicited inbound request are routed from that single read - concurrent callers awaiting a correlated
/// response can never compete with each other, or with request handling, to read the same stream directly.
/// </summary>
internal sealed class ControlPipeDuplex : IAsyncDisposable
{
    public const int ProtocolVersion = 1;

    private readonly PipeStream myPipe;
    private readonly StreamReader myReader;
    private readonly StreamWriter myWriter;
    private readonly SemaphoreSlim myWriteLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> myPending = new(StringComparer.Ordinal);
    private readonly Func<JsonElement, CancellationToken, Task> myOnUnsolicited;
    private Task? myDispatchLoop;

    public ControlPipeDuplex(PipeStream pipe, Func<JsonElement, CancellationToken, Task> onUnsolicited)
    {
        myPipe = pipe;
        myReader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        myWriter = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        myOnUnsolicited = onUnsolicited;
    }

    /// <summary>Starts the single background reader that owns every read from the pipe for this endpoint's
    /// lifetime.</summary>
    public void StartDispatching() => myDispatchLoop = RunDispatchLoopAsync();

    /// <summary>Sends one envelope with an explicit correlation id and does not wait for a reply.</summary>
    public async Task SendAsync(string type, string correlationId, object? payload, CancellationToken cancellationToken)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["version"] = ProtocolVersion,
            ["type"] = type,
            ["correlationId"] = correlationId,
        };
        if (payload is not null)
        {
            envelope["payload"] = payload;
        }
        var line = JsonSerializer.Serialize(envelope);
        await myWriteLock.WaitAsync(cancellationToken);
        try
        {
            await myWriter.WriteLineAsync(line.AsMemory(), cancellationToken);
            await myWriter.FlushAsync(cancellationToken);
        }
        finally
        {
            myWriteLock.Release();
        }
    }

    /// <summary>Sends one envelope under a freshly generated correlation id and awaits the reply the dispatch
    /// loop routes back under that same id - the request half of the request/acknowledgement exchange.</summary>
    public async Task<JsonElement> SendAndAwaitAsync(string type, object? payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("n");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        myPending[correlationId] = tcs;
        try
        {
            await SendAsync(type, correlationId, payload, cancellationToken);
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            await using var timeoutRegistration = timeoutCancellation.Token.Register(() =>
                tcs.TrySetException(new TimeoutException($"Timed out waiting for a reply to '{type}' (correlation '{correlationId}').")))
                .ConfigureAwait(false);
            await using var cancelRegistration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken))
                .ConfigureAwait(false);
            return await tcs.Task;
        }
        finally
        {
            myPending.TryRemove(correlationId, out _);
        }
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> if the given reply to a
    /// <paramref name="requestType"/> request is an explicit protocol-error envelope, for either endpoint to
    /// interpret its own <see cref="SendAndAwaitAsync"/> replies consistently.</summary>
    public static void EnsureNotProtocolError(JsonElement response, string requestType)
    {
        if (!response.TryGetProperty("type", out var typeElement) || typeElement.GetString() != "protocol-error")
        {
            return;
        }
        var message = response.TryGetProperty("payload", out var payload)
            && payload.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : "(no message)";
        throw new InvalidOperationException($"The fake-provider control pipe rejected '{requestType}': {message}");
    }

    private async Task RunDispatchLoopAsync()
    {
        while (true)
        {
            string? line;
            try
            {
                line = await myReader.ReadLineAsync();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                break;
            }
            if (line is null)
            {
                break;
            }

            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            var correlationId = root.TryGetProperty("correlationId", out var correlationElement)
                && correlationElement.ValueKind == JsonValueKind.String
                ? correlationElement.GetString()
                : null;

            if (correlationId is not null && myPending.TryRemove(correlationId, out var pending))
            {
                pending.TrySetResult(root);
                continue;
            }

            _ = myOnUnsolicited(root, CancellationToken.None);
        }

        FailAllPending(new IOException("The fake-provider control pipe closed before a reply arrived."));
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var correlationId in myPending.Keys.ToArray())
        {
            if (myPending.TryRemove(correlationId, out var pending))
            {
                pending.TrySetException(exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The writer flushes safely while the pipe is still open (it already auto-flushes every write). Disposing
        // the pipe next unblocks the dispatch loop's pending read - it is almost always blocked inside one, which
        // only ever completes on new data, the peer closing, or the pipe itself being disposed - so awaiting the
        // loop afterward completes instead of deadlocking forever on its own pending read.
        await myWriter.DisposeAsync();
        await myPipe.DisposeAsync();
        if (myDispatchLoop is not null)
        {
            try
            {
                await myDispatchLoop;
            }
            catch
            {
                // Best-effort: disposal must never fail because the dispatch loop observed a closed pipe.
            }
        }
        myReader.Dispose();
    }
}
