using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>
/// Minimal, test-owned raw client for the fake-provider control pipe, used only by this transport's own focused
/// specification to send deliberately malformed envelopes - an invalid token, an unsupported version, a missing
/// correlation id, or an unknown command - and read back <see cref="FakeProviderControlServer"/>'s explicit
/// protocol-error diagnostics. Unlike <see cref="FakeProviderControlClient"/>, it sends exactly one hand-built
/// line at a time and reads exactly one reply, with no dispatch loop or correlation bookkeeping, because these
/// negative-path checks never need concurrent requests.
/// </summary>
internal sealed class RawControlPipeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream myPipe;
    private readonly StreamReader myReader;
    private readonly StreamWriter myWriter;

    private RawControlPipeClient(NamedPipeClientStream pipe, StreamReader reader, StreamWriter writer)
    {
        myPipe = pipe;
        myReader = reader;
        myWriter = writer;
    }

    public static async Task<RawControlPipeClient> ConnectAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);
        var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        return new RawControlPipeClient(pipe, reader, writer);
    }

    /// <summary>Sends one raw, hand-built JSON line exactly as given - deliberately malformed for negative-path
    /// specs, unlike the well-formed envelopes the production client always sends.</summary>
    public Task SendRawLineAsync(string json, CancellationToken cancellationToken = default) =>
        myWriter.WriteLineAsync(json.AsMemory(), cancellationToken);

    /// <summary>Reads back exactly one response line, parsed for assertion.</summary>
    public async Task<JsonElement> ReadResponseAsync(CancellationToken cancellationToken = default)
    {
        var line = await myReader.ReadLineAsync(cancellationToken)
            ?? throw new IOException("The fake-provider control pipe closed before a response arrived.");
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        myReader.Dispose();
        await myWriter.DisposeAsync();
        await myPipe.DisposeAsync();
    }
}
