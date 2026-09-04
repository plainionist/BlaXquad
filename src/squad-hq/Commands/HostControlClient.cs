using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace squad_hq.Commands;

public static class HostControlClient
{
    public static async Task WaitForAgentAsync(string projectRoot, string role, TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        projectRoot = Path.GetFullPath(projectRoot);
        var elapsed = Stopwatch.StartNew();
        var lastStatus = "squad host unavailable";
        while (elapsed.Elapsed < timeout)
        {
            var remaining = timeout - elapsed.Elapsed;
            var status = await QueryAgentStatusAsync(projectRoot, role, remaining);
            if (status == "ready")
                return;
            if (status == "unknown-role")
                throw new InvalidOperationException($"The squad has no agent role named '{role}'.");
            lastStatus = status == "not-ready" ? "agent not ready" : status;
            remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;
            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(100)
                    ? remaining
                    : TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException(
            $"Agent '{role}' did not become ready within {timeout.TotalSeconds:0.###} seconds ({lastStatus}).");
    }

    public static async Task<bool> ShutdownAsync(string projectRoot, TimeSpan timeout)
    {
        if (!await RequestShutdownAsync(projectRoot))
            return false;

        projectRoot = Path.GetFullPath(projectRoot);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !HostLease.TryAcquireProbe(projectRoot))
            await Task.Delay(100);
        if (!HostLease.TryAcquireProbe(projectRoot))
            throw new TimeoutException("The squad host did not shut down within 15 seconds.");
        return true;
    }

    public static async Task<bool> RequestShutdownAsync(string projectRoot)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var stateDir = Path.Combine(projectRoot, ".blaxquad");
        if (!Directory.Exists(stateDir))
            return false;
        if (HostLease.RemoveStaleMetadata(projectRoot))
            return false;
        var pipeName = HostLease.PipeNameFor(projectRoot);

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await pipe.ConnectAsync(connectTimeout.Token);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
        {
            if (HostLease.RemoveStaleMetadata(projectRoot))
                return false;
            throw new IOException("The squad host metadata exists, but its control pipe is unavailable while the host lock is held.", exception);
        }
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("{\"version\":1,\"command\":\"shutdown\"}");
        await reader.ReadLineAsync(connectTimeout.Token);
        return true;
    }

    private static async Task<string> QueryAgentStatusAsync(string projectRoot, string role, TimeSpan remaining)
    {
        var stateDir = Path.Combine(projectRoot, ".blaxquad");
        if (!Directory.Exists(stateDir) || HostLease.RemoveStaleMetadata(projectRoot))
            return "squad host unavailable";

        var pipeName = HostLease.PipeNameFor(projectRoot);
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var queryElapsed = Stopwatch.StartNew();
        var connectDuration = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
        var connectMilliseconds = Math.Max(1, (int)Math.Ceiling(connectDuration.TotalMilliseconds));
        try
        {
            pipe.Connect(connectMilliseconds);
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException)
        {
            if (HostLease.RemoveStaleMetadata(projectRoot))
                return "squad host unavailable";
            return "squad control endpoint unavailable";
        }

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        var ioRemaining = remaining - queryElapsed.Elapsed;
        if (ioRemaining <= TimeSpan.Zero)
            return "squad control endpoint unavailable";
        var ioDuration = ioRemaining < TimeSpan.FromSeconds(1)
            ? ioRemaining
            : TimeSpan.FromSeconds(1);
        using var operationTimeout = new CancellationTokenSource(ioDuration);
        string? response;
        try
        {
            var request = JsonSerializer.Serialize(new
            {
                version = 1,
                command = "agent-status",
                role,
            });
            await writer.WriteLineAsync(request.AsMemory(), operationTimeout.Token);
            response = await reader.ReadLineAsync(operationTimeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return HostLease.RemoveStaleMetadata(projectRoot)
                ? "squad host unavailable"
                : "squad control endpoint unavailable";
        }
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidDataException("The squad host returned an empty readiness response.");
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out var version)
            || !version.TryGetInt32(out var versionNumber)
            || versionNumber != 1
            || !root.TryGetProperty("status", out var responseStatus)
            || responseStatus.ValueKind != JsonValueKind.String
            || responseStatus.GetString() != "ok"
            || !root.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("The squad host returned an invalid readiness response.");
        return message.GetString()!;
    }
}



