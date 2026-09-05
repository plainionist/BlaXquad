using squad.Process;
using squad.Hosting.Abstractions;
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
            myUnixInhibitor = ProcessControl.StartDetached(command);
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
                await ProcessControl.TerminateAsync(inhibitor);
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

        if (OperatingSystem.IsMacOS() && ProcessControl.CommandExists("caffeinate"))
            return ["caffeinate", "-dims"];
        if (OperatingSystem.IsLinux() &&
            ProcessControl.CommandExists("systemd-inhibit") &&
            ProcessControl.CommandExists("systemctl") &&
            LinuxSystemdRunning())
            return ["systemd-inhibit", "--what=sleep:idle", "--who=squad", "--why=squad is active"];
        return [];
    }

    private static bool LinuxSystemdRunning()
    {
        var state = ProcessRunner.Run("systemctl", ["is-system-running"]).StdOut.Trim();
        return state is "running" or "degraded";
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint executionState);
}



