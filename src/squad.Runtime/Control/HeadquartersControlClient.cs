using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace squad.Runtime.Control;

/// <summary>Communicates with the live Headquarters instance identified by a project root and cleans up stale Headquarters metadata.</summary>
public static class HeadquartersControlClient
{
    /// <summary>
    /// Polls until a known role is provider-ready, distinguishing unknown roles and unavailable Headquarters instances in failures.
    /// </summary>
    public static async Task WaitForAgentAsync(string projectRoot, string role, TimeSpan timeout)
    {
        Contract.Requires(timeout > TimeSpan.Zero, "timeout must be positive.");
        projectRoot = Path.GetFullPath(projectRoot);
        var elapsed = Stopwatch.StartNew();
        var lastStatus = AgentReadinessStatus.Unavailable;
        while (elapsed.Elapsed < timeout)
        {
            var remaining = timeout - elapsed.Elapsed;
            var status = await QueryAgentStatusAsync(projectRoot, role, remaining);
            if (status == AgentReadinessStatus.Ready)
            {
                return;
            }
            if (status == AgentReadinessStatus.UnknownRole)
            {
                throw new InvalidOperationException($"The squad has no agent role named '{role}'.");
            }
            if (status == AgentReadinessStatus.NotReady)
            {
                lastStatus = AgentReadinessStatus.NotReady;
            }
            else if (lastStatus == AgentReadinessStatus.Unavailable)
            {
                lastStatus = status;
            }
            remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }
            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(100)
                    ? remaining
                    : TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException(
            $"Agent '{role}' did not become ready within {timeout.TotalSeconds:0.###} seconds ({DescribeForTimeout(lastStatus)}).");
    }

    /// <summary>Derives <see cref="WaitForAgentAsync"/>'s existing user-facing timeout detail from an exhaustive
    /// switch over every non-terminal status it can still be holding when the wait loop times out.</summary>
    private static string DescribeForTimeout(AgentReadinessStatus status) => status switch
    {
        AgentReadinessStatus.NotReady => "agent not ready",
        AgentReadinessStatus.Initializing => "initializing",
        AgentReadinessStatus.Unavailable => "Headquarters unavailable",
        AgentReadinessStatus.EndpointUnavailable => "Headquarters control endpoint unavailable",
        AgentReadinessStatus.Ready or AgentReadinessStatus.UnknownRole =>
            throw new InvalidOperationException($"unreachable: {status} never reaches the timeout path."),
        _ => throw new InvalidOperationException($"unknown agent readiness status {status}"),
    };

    /// <summary>Requests shutdown and waits until Headquarters releases project ownership.</summary>
    public static async Task<bool> ShutdownAsync(string projectRoot, TimeSpan timeout)
    {
        Contract.Requires(timeout > TimeSpan.Zero, "timeout must be positive.");
        if (!await RequestShutdownAsync(projectRoot))
        {
            return false;
        }

        projectRoot = Path.GetFullPath(projectRoot);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !HeadquartersLease.TryAcquireProbe(projectRoot))
        {
            await Task.Delay(100);
        }
        if (!HeadquartersLease.TryAcquireProbe(projectRoot))
        {
            throw new TimeoutException("Headquarters did not shut down within 15 seconds.");
        }
        return true;
    }

    /// <summary>Returns <see langword="false"/> when no live Headquarters instance exists; otherwise sends but does not await shutdown.</summary>
    private static async Task<bool> RequestShutdownAsync(string projectRoot)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var stateDir = Path.Combine(projectRoot, ".blaxquad");
        if (!Directory.Exists(stateDir))
        {
            return false;
        }
        if (HeadquartersLease.RemoveStaleMetadata(projectRoot))
        {
            return false;
        }
        var pipeName = HeadquartersLease.PipeNameFor(projectRoot);

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await pipe.ConnectAsync(connectTimeout.Token);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
        {
            if (HeadquartersLease.RemoveStaleMetadata(projectRoot))
            {
                return false;
            }
            throw new IOException("Headquarters metadata exists, but its control pipe is unavailable while the Headquarters lock is held.", exception);
        }
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("{\"version\":1,\"command\":\"shutdown\"}");
        await reader.ReadLineAsync(connectTimeout.Token);
        return true;
    }

    private static async Task<AgentReadinessStatus> QueryAgentStatusAsync(string projectRoot, string role, TimeSpan remaining)
    {
        var stateDir = Path.Combine(projectRoot, ".blaxquad");
        if (!Directory.Exists(stateDir) || HeadquartersLease.RemoveStaleMetadata(projectRoot))
        {
            return AgentReadinessStatus.Unavailable;
        }

        var pipeName = HeadquartersLease.PipeNameFor(projectRoot);
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
            if (HeadquartersLease.RemoveStaleMetadata(projectRoot))
            {
                return AgentReadinessStatus.Unavailable;
            }
            return AgentReadinessStatus.EndpointUnavailable;
        }

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        var ioRemaining = remaining - queryElapsed.Elapsed;
        if (ioRemaining <= TimeSpan.Zero)
        {
            return AgentReadinessStatus.EndpointUnavailable;
        }
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
            return HeadquartersLease.RemoveStaleMetadata(projectRoot)
                ? AgentReadinessStatus.Unavailable
                : AgentReadinessStatus.EndpointUnavailable;
        }
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidDataException("Headquarters returned an empty readiness response.");
        }
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
        {
            throw new InvalidDataException("Headquarters returned an invalid readiness response.");
        }
        return message.GetString() switch
        {
            "ready" => AgentReadinessStatus.Ready,
            "not-ready" => AgentReadinessStatus.NotReady,
            "unknown-role" => AgentReadinessStatus.UnknownRole,
            "initializing" => AgentReadinessStatus.Initializing,
            _ => throw new InvalidDataException("Headquarters returned an invalid readiness response."),
        };
    }
}


