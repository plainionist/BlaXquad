using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace squad.Specs.Support;

/// <summary>
/// Test-owned named-pipe control channel between the scenario driver (this test process) and the
/// <see cref="ControllableAgentProviderFactory"/> fake provider running inside the real, separately launched
/// squad-hq process. The endpoint name travels through the <see
/// cref="ControllableAgentProviderFactory.PipeNameEnvironmentVariable"/> environment variable set on the launched
/// process; squad-hq itself never parses, forwards, or understands this protocol. Every exchange is one
/// newline-delimited JSON line, and every command receives a matching acknowledgement so the scenario driver can
/// wait deterministically instead of sleeping. Both ends and every DTO live in this test project only.
/// </summary>
public sealed class UsageControlServer : IAsyncDisposable
{
    private readonly NamedPipeServerStream myPipe;
    private StreamReader? myReader;
    private StreamWriter? myWriter;

    private UsageControlServer(string pipeName)
    {
        PipeName = pipeName;
        myPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    public string PipeName { get; }

    public static UsageControlServer Create() => new($"blaxquad-usage-test-{Guid.NewGuid():N}");

    public async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        await myPipe.WaitForConnectionAsync(cancellationToken);
        myReader = new StreamReader(myPipe, new UTF8Encoding(false), leaveOpen: true);
        myWriter = new StreamWriter(myPipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    }

    public async Task WaitForSessionStartedAsync(string role, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await myReader!.ReadLineAsync(cancellationToken)
                ?? throw new IOException("The usage control pipe closed before the provider session started.");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("event", out var eventElement)
                && eventElement.GetString() == "started"
                && root.TryGetProperty("role", out var roleElement)
                && roleElement.GetString() == role)
            {
                return;
            }
        }
    }

    public Task SendUsageAsync(string role, long contextUsed, long contextLimit, decimal aicUsed, CancellationToken cancellationToken) =>
        SendCommandAsync("usage", role, contextUsed, contextLimit, aicUsed, cancellationToken);

    public Task SendIdleAsync(string role, long contextUsed, long contextLimit, decimal aicUsed, CancellationToken cancellationToken) =>
        SendCommandAsync("idle", role, contextUsed, contextLimit, aicUsed, cancellationToken);

    private async Task SendCommandAsync(string command, string role, long contextUsed, long contextLimit, decimal aicUsed, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(new { command, role, contextUsed, contextLimit, aicUsed });
        await myWriter!.WriteLineAsync(request.AsMemory(), cancellationToken);
        var response = await myReader!.ReadLineAsync(cancellationToken)
            ?? throw new IOException($"The usage control pipe closed before acknowledging '{command}'.");
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("ack", out var ack) || ack.GetString() != command)
        {
            throw new InvalidDataException($"Expected an ack for '{command}' but received: {response}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        myReader?.Dispose();
        if (myWriter is not null)
        {
            await myWriter.DisposeAsync();
        }
        await myPipe.DisposeAsync();
    }
}

/// <summary>Provider-side half of <see cref="UsageControlServer"/>, loaded into the squad-hq process.</summary>
internal sealed class UsageControlClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream myPipe;
    private readonly StreamReader myReader;
    private readonly StreamWriter myWriter;

    private UsageControlClient(NamedPipeClientStream pipe, StreamReader reader, StreamWriter writer)
    {
        myPipe = pipe;
        myReader = reader;
        myWriter = writer;
    }

    public static async Task<UsageControlClient> ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);
        var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        return new UsageControlClient(pipe, reader, writer);
    }

    public Task NotifyStartedAsync(string role, CancellationToken cancellationToken) =>
        myWriter.WriteLineAsync(JsonSerializer.Serialize(new { @event = "started", role }).AsMemory(), cancellationToken);

    /// <summary>Reads commands until the pipe closes, dispatching each to the matching role's session and
    /// acknowledging it once the corresponding events have been published.</summary>
    public async Task RunAsync(IReadOnlyDictionary<string, ControllableAgentSession> sessionsByRole, CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await myReader.ReadLineAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
            {
                return;
            }
            if (line is null)
            {
                return;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var command = root.GetProperty("command").GetString()!;
            var role = root.GetProperty("role").GetString()!;
            var contextUsed = root.GetProperty("contextUsed").GetInt64();
            var contextLimit = root.GetProperty("contextLimit").GetInt64();
            var aicUsed = root.GetProperty("aicUsed").GetDecimal();

            if (sessionsByRole.TryGetValue(role, out var session))
            {
                switch (command)
                {
                    case "usage":
                        session.PublishUsage(contextUsed, contextLimit, aicUsed);
                        break;
                    case "idle":
                        session.GoIdle(contextUsed, contextLimit, aicUsed);
                        break;
                }
            }

            await myWriter.WriteLineAsync(JsonSerializer.Serialize(new { ack = command }).AsMemory(), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        myReader.Dispose();
        await myWriter.DisposeAsync();
        await myPipe.DisposeAsync();
    }
}
