using global::squad.Process;
using global::squad.Hosting.Abstractions;
using System.Diagnostics;

namespace squad.Photino;

public sealed class SleepInhibitor : ISleepInhibitor
{
    private const uint myEsContinuous = 0x80000000;
    private const uint myEsSystemRequired = 0x00000001;
    private const uint myEsDisplayRequired = 0x00000002;
    private readonly Lazy<IReadOnlyList<string>> myCommandPrefix = new(DetectPrefix);
    private Thread? myWindowsThread;
    private AutoResetEvent? myWindowsStop;
    private TaskCompletionSource? myWindowsReady;
    private TaskCompletionSource? myWindowsStopped;
    private Exception? myWindowsFailure;
    private System.Diagnostics.Process? myUnixInhibitor;

    public IReadOnlyList<string> CommandPrefix => myCommandPrefix.Value;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Environment.GetEnvironmentVariable("BLAXQUAD_PREVENT_SLEEP") == "0")
            return;
        if (OperatingSystem.IsWindows())
        {
            if (myWindowsThread is not null)
                return;

            myWindowsStop = new AutoResetEvent(false);
            myWindowsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            myWindowsStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            myWindowsFailure = null;
            myWindowsThread = new Thread(RunWindowsInhibitor) { IsBackground = true, Name = "BlaXquad sleep inhibitor" };
            myWindowsThread.Start();
            try
            {
                await myWindowsReady.Task.WaitAsync(cancellationToken);
            }
            catch
            {
                await StopWindowsInhibitorAsync();
                throw;
            }
        }
        else if (myUnixInhibitor is null && CommandPrefix.Count > 0)
        {
            var command = CommandPrefix[0] == "caffeinate"
                ? CommandPrefix
                : CommandPrefix.Concat(["sleep", "infinity"]).ToArray();
            myUnixInhibitor = StartDetached(command);
            if (myUnixInhibitor.HasExited)
            {
                var exitCode = myUnixInhibitor.ExitCode;
                myUnixInhibitor.Dispose();
                myUnixInhibitor = null;
                throw new InvalidOperationException($"Sleep inhibitor exited with code {exitCode}.");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                await StopUnixInhibitorAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopUnixInhibitorAsync();
        await StopWindowsInhibitorAsync();
    }

    private async Task StopUnixInhibitorAsync()
    {
        if (myUnixInhibitor is not { } inhibitor)
            return;

        var terminatedByOwner = false;
        try
        {
            if (!inhibitor.HasExited)
            {
                terminatedByOwner = true;
                await TerminateAsync(inhibitor);
            }
            if (!terminatedByOwner && inhibitor.ExitCode != 0)
                throw new InvalidOperationException($"Sleep inhibitor exited with code {inhibitor.ExitCode}.");
        }
        finally
        {
            inhibitor.Dispose();
            myUnixInhibitor = null;
        }
    }

    private void RunWindowsInhibitor()
    {
        try
        {
            if (SetThreadExecutionState(myEsContinuous | myEsSystemRequired | myEsDisplayRequired) == 0)
                throw new InvalidOperationException("Could not enable Windows sleep prevention.");
            myWindowsReady!.TrySetResult();
            myWindowsStop!.WaitOne();
            if (SetThreadExecutionState(myEsContinuous) == 0)
                throw new InvalidOperationException("Could not reset Windows sleep prevention.");
        }
        catch (Exception exception)
        {
            myWindowsFailure = exception;
            myWindowsReady!.TrySetException(exception);
        }
        finally
        {
            myWindowsStopped!.TrySetResult();
        }
    }

    private async Task StopWindowsInhibitorAsync()
    {
        if (myWindowsThread is null)
            return;

        myWindowsStop!.Set();
        await myWindowsStopped!.Task;
        var failure = myWindowsFailure;
        myWindowsStop.Dispose();
        myWindowsThread = null;
        myWindowsStop = null;
        myWindowsReady = null;
        myWindowsStopped = null;
        myWindowsFailure = null;
        if (failure is not null)
            throw failure;
    }

    private static IReadOnlyList<string> DetectPrefix()
    {
        if (Environment.GetEnvironmentVariable("BLAXQUAD_PREVENT_SLEEP") == "0")
            return [];

        if (OperatingSystem.IsMacOS() && ExecutableLocator.Exists("caffeinate"))
            return ["caffeinate", "-dims"];
        if (OperatingSystem.IsLinux() &&
            ExecutableLocator.Exists("systemd-inhibit") &&
            ExecutableLocator.Exists("systemctl") &&
            LinuxSystemdRunning())
            return ["systemd-inhibit", "--what=sleep:idle", "--who=squad", "--why=squad is active"];
        return [];
    }

    private static bool LinuxSystemdRunning()
    {
        var state = ProcessRunner.Run("systemctl", ["is-system-running"]).StdOut.Trim();
        return state is "running" or "degraded";
    }

    /// <summary>Starts a detached child process owned and terminated only by this sleep inhibitor.</summary>
    private static System.Diagnostics.Process StartDetached(IReadOnlyList<string> command, string? stdOutErrFile = null)
    {
        if (command.Count == 0)
            throw new ArgumentException("Command must not be empty.", nameof(command));

        var psi = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = stdOutErrFile is not null,
            RedirectStandardError = stdOutErrFile is not null,
        };
        foreach (var argument in command.Skip(1))
            psi.ArgumentList.Add(argument);

        var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{command[0]}'.");
        if (stdOutErrFile is not null)
            _ = CaptureOutputAsync(process, stdOutErrFile);
        return process;
    }

    private static async Task TerminateAsync(System.Diagnostics.Process process)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static async Task CaptureOutputAsync(System.Diagnostics.Process process, string outputFile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            await using var writer = new StreamWriter(new FileStream(outputFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
            var synchronizedWriter = TextWriter.Synchronized(writer);
            await Task.WhenAll(
                PumpOutputAsync(process.StandardOutput, synchronizedWriter),
                PumpOutputAsync(process.StandardError, synchronizedWriter));
        }
        catch
        {
            // Detached process output capture must not terminate its owner.
        }
    }

    private static async Task PumpOutputAsync(StreamReader reader, TextWriter writer)
    {
        while (await reader.ReadLineAsync() is { } line)
            await writer.WriteLineAsync(line);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint executionState);
}



