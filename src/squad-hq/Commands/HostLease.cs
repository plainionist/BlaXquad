using squad.Process;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace squadHQ.Commands;

public sealed class HostLease : IHostLease
{
    private readonly string myProjectRoot;
    private readonly string myStateDir;
    private readonly FileStream myLockFile;
    private readonly CancellationTokenSource myShutdown = new();
    private readonly TaskCompletionSource myShutdownRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myServerFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string myPipeName;
    private Func<string, CancellationToken, Task<bool?>>? myAgentReadinessProvider;
    private Task? myServer;
    private bool myDisposed;

    private HostLease(string projectRoot, FileStream lockFile, string pipeName)
    {
        myProjectRoot = projectRoot;
        myStateDir = Path.Combine(projectRoot, ".blaxquad");
        myLockFile = lockFile;
        myPipeName = pipeName;
        ShutdownRequested = myShutdownRequested.Task;
        ServerFailure = myServerFailure.Task;
    }

    public string PipeName => myPipeName;
    public Task ShutdownRequested { get; }
    public Task ServerFailure { get; }

    public void SetAgentReadinessProvider(Func<string, CancellationToken, Task<bool?>> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Volatile.Write(ref myAgentReadinessProvider, provider);
    }

    public static HostLease Acquire(string projectRoot)
    {
        projectRoot = NormalizeProjectRoot(projectRoot);
        var stateDir = Path.Combine(projectRoot, ".blaxquad");
        Directory.CreateDirectory(stateDir);
        var lockPath = Path.Combine(stateDir, "host.lock");
        FileStream? lockFile = null;
        try
        {
            lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            LockFile(lockFile);
        }
        catch (IOException)
        {
            lockFile?.Dispose();
            throw new CliExitException(1, $"A squad host is already running for {projectRoot}.");
        }

        try
        {
            var pipeName = PipeNameFor(projectRoot);
            var lease = new HostLease(projectRoot, lockFile!, pipeName);
            lease.WriteMetadata();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lease.myServer = lease.RunServerAsync(ready);
            ready.Task.GetAwaiter().GetResult();
            return lease;
        }
        catch
        {
            var metadata = Path.Combine(stateDir, "host.json");
            try
            {
                if (File.Exists(metadata))
                    File.Delete(metadata);
            }
            finally
            {
                try { UnlockFile(lockFile!); } catch { }
                lockFile!.Dispose();
            }
            throw;
        }
    }

    public static bool RemoveStaleMetadata(string projectRoot)
    {
        if (!TryAcquireCleanupLease(projectRoot, out var lease))
            return false;
        var cleanupLease = lease!;
        using (cleanupLease)
            cleanupLease.RemoveStaleMetadata();
        return true;
    }

    public static bool TryAcquireProbe(string projectRoot)
    {
        if (!TryAcquireCleanupLease(projectRoot, out var lease))
            return false;
        lease!.Dispose();
        return true;
    }

    public static bool TryAcquireCleanupLease(string projectRoot, out CleanupLease? lease)
    {
        projectRoot = NormalizeProjectRoot(projectRoot);
        var stateDir = Path.Combine(projectRoot, ".blaxquad");
        Directory.CreateDirectory(stateDir);
        var lockPath = Path.Combine(stateDir, "host.lock");
        FileStream? lockFile = null;
        try
        {
            lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            LockFile(lockFile);
            lease = new CleanupLease(stateDir, lockFile);
            return true;
        }
        catch (IOException)
        {
            lockFile?.Dispose();
            lease = null;
            return false;
        }
    }

    public static string PipeNameFor(string projectRoot) =>
        "blaxquad-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(IdentityProjectRoot(projectRoot)))).ToLowerInvariant()[..24];

    private static string NormalizeProjectRoot(string projectRoot)
    {
        var fullPath = Path.GetFullPath(projectRoot);
        var root = Path.GetPathRoot(fullPath);
        if (fullPath.Length > root!.Length)
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath;
    }

    private static string IdentityProjectRoot(string projectRoot) =>
        OperatingSystem.IsWindows() ? NormalizeProjectRoot(projectRoot).ToUpperInvariant() : NormalizeProjectRoot(projectRoot);

    private void WriteMetadata()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            version = 1,
            pid = Environment.ProcessId,
            projectRoot = myProjectRoot,
            controlPipe = myPipeName,
            startedAt = DateTimeOffset.UtcNow,
        });
        var target = Path.Combine(myStateDir, "host.json");
        var temporary = target + $".tmp.{Guid.NewGuid():N}";
        File.WriteAllText(temporary, metadata + Environment.NewLine);
        File.Move(temporary, target, overwrite: true);
    }

    private async Task RunServerAsync(TaskCompletionSource ready)
    {
        while (!myShutdown.IsCancellationRequested)
        {
            var listenerCreated = false;
            try
            {
                await using var pipe = new NamedPipeServerStream(myPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                ready.TrySetResult();
                listenerCreated = true;
                await pipe.WaitForConnectionAsync(myShutdown.Token);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var request = ParseRequest(await reader.ReadLineAsync(myShutdown.Token));
                if (request?.Command == "shutdown")
                    myShutdownRequested.TrySetResult();
                await writer.WriteLineAsync(await CreateResponseAsync(request, myShutdown.Token));
            }
            catch (OperationCanceledException) when (myShutdown.IsCancellationRequested)
            {
                ready.TrySetCanceled();
                break;
            }
            catch (Exception exception)
            {
                if (!listenerCreated)
                {
                    if (ready.Task.IsCompletedSuccessfully)
                        myServerFailure.TrySetException(exception);
                    else
                        ready.TrySetException(exception);
                    return;
                }
            }
        }
    }

    private async Task<string> CreateResponseAsync(
        HostControlRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return """{"version":1,"status":"error","message":"invalid request"}""";
        if (request.Command == "agent-status")
        {
            if (string.IsNullOrWhiteSpace(request.Role))
                return """{"version":1,"status":"error","message":"role is required"}""";
            var provider = Volatile.Read(ref myAgentReadinessProvider);
            var readiness = provider is null
                ? null
                : await provider(request.Role, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                version = 1,
                status = "ok",
                message = provider is null
                    ? "initializing"
                    : readiness switch
                    {
                        true => "ready",
                        false => "not-ready",
                        null => "unknown-role",
                    },
                role = request.Role,
            });
        }
        return $"{{\"version\":1,\"status\":\"ok\",\"message\":\"{request.Command}\"}}";
    }

    private static HostControlRequest? ParseRequest(string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
            return null;
        try
        {
            using var document = JsonDocument.Parse(request);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || !version.TryGetInt32(out var versionNumber)
                || versionNumber != 1
                || !root.TryGetProperty("command", out var commandElement)
                || commandElement.ValueKind != JsonValueKind.String)
                return null;
            var command = commandElement.GetString();
            if (command is not ("ping" or "shutdown" or "agent-status"))
                return null;
            var role = root.TryGetProperty("role", out var roleElement)
                && roleElement.ValueKind == JsonValueKind.String
                ? roleElement.GetString()
                : null;
            return new HostControlRequest(command, role);
        }
        catch (JsonException) { return null; }
    }

    public async ValueTask DisposeAsync()
    {
        if (myDisposed)
            return;
        myDisposed = true;
        myShutdown.Cancel();
        try
        {
            if (myServer is not null)
                await myServer;
        }
        finally
        {
            var metadata = Path.Combine(myStateDir, "host.json");
            try
            {
                if (File.Exists(metadata))
                    File.Delete(metadata);
            }
            finally
            {
                try { UnlockFile(myLockFile); } catch (Exception) { }
                try { await myLockFile.DisposeAsync(); } catch (Exception) { }
                myShutdown.Dispose();
            }
        }
    }

    private static void LockFile(FileStream file)
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            if (flock((int)file.SafeFileHandle.DangerousGetHandle(), 6) != 0)
                throw new IOException("The host lock is already held.");
            return;
        }

        #pragma warning disable CA1416
        file.Lock(0, 1);
        #pragma warning restore CA1416
    }

    internal static void UnlockFile(FileStream file)
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            _ = flock((int)file.SafeFileHandle.DangerousGetHandle(), 8);
            return;
        }

        #pragma warning disable CA1416
        file.Unlock(0, 1);
        #pragma warning restore CA1416
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int flock(int fileDescriptor, int operation);

}



