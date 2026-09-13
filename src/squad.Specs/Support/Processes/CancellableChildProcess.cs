using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace squad.Specs.Support.Processes;

/// <summary>
/// The one narrow support type that hides every native handle, process-group, and signal-delivery detail behind
/// two plain operations: launching a child process that owns its own process group, and delivering the
/// platform's own normal cancellation signal (the same one a real terminal's Ctrl+C/Ctrl+Break produces) to that
/// exact child - never the test runner process, and never any other concurrently running scenario's own child.
/// Ordinary launches keep using <see cref="Support.ScenarioWorkspace.StartProcess"/> and plain <see cref="System.Diagnostics.Process.Start()"/>;
/// this type exists solely so the caller-cancellation specification scenario can prove real signal delivery
/// without ever risking the shared test-runner process group. No product command, environment variable, or
/// in-process shortcut depends on this type.
/// </summary>
public static class CancellableChildProcess
{
    /// <summary>
    /// Whether this platform lets a caller isolate signal delivery to exactly one child process's own group. When
    /// false, <see cref="Start"/> and <see cref="SendCancellationSignal"/> throw a clear, explicit
    /// unsupported-platform diagnostic instead of ever falling back to a broadcast that could also reach the test
    /// runner or another scenario's own child.
    /// </summary>
    public static bool CanDeliverIsolatedSignal => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Launches the given executable as the sole owner of a brand-new process group (Windows) or session-leading
    /// process group (Unix), with its three standard streams redirected exactly like an ordinary
    /// <see cref="System.Diagnostics.Process.Start()"/> launch, so <see cref="SendCancellationSignal"/> can later
    /// target that process's own group without ever affecting any other process.
    /// </summary>
    public static System.Diagnostics.Process Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        out TextWriter standardInput,
        out TextReader standardOutput,
        out TextReader standardError)
    {

        if (OperatingSystem.IsWindows())
        {
            return WindowsProcessGroup.Start(executable, arguments, workingDirectory, environment, out standardInput, out standardOutput, out standardError);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return UnixProcessGroup.Start(executable, arguments, workingDirectory, environment, out standardInput, out standardOutput, out standardError);
        }

        throw new PlatformNotSupportedException(
            "CancellableChildProcess can only isolate signal delivery to one child process's own group on Windows, Linux, and macOS.");
    }

    /// <summary>
    /// Delivers the platform's own normal cancellation signal - Windows CTRL_BREAK_EVENT, Unix SIGINT - to exactly
    /// the process group owned by the given process (which must have been created by <see cref="Start"/>), the
    /// same signal a real terminal's Ctrl+C would send to a foreground process, without ever broadcasting to the
    /// test runner's own console or process group.
    /// </summary>
    public static void SendCancellationSignal(System.Diagnostics.Process process)
    {

        if (OperatingSystem.IsWindows())
        {
            WindowsProcessGroup.SendCancellationSignal(process);
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            UnixProcessGroup.SendCancellationSignal(process);
            return;
        }

        throw new PlatformNotSupportedException(
            "CancellableChildProcess can only isolate signal delivery to one child process's own group on Windows, Linux, and macOS.");
    }

    /// <summary>
    /// Reads the exit code of a process created by <see cref="Start"/>. On Windows this reads the exit code
    /// directly from the operating system by process id rather than through
    /// <see cref="System.Diagnostics.Process.ExitCode"/>, because that property throws for a process adopted via
    /// <see cref="System.Diagnostics.Process.GetProcessById(int)"/> unless something else already made .NET cache
    /// its own process handle - which merely observing <c>HasExited</c>/<c>WaitForExit</c> never does. On other
    /// platforms, where <see cref="Start"/> launches through the ordinary <see cref="System.Diagnostics.Process.Start()"/>
    /// API, the process handle is always cached from the start, so the managed property works as normal.
    /// </summary>
    public static int GetExitCode(System.Diagnostics.Process process)
    {

        if (OperatingSystem.IsWindows())
        {
            return WindowsProcessGroup.GetExitCode(process.Id);
        }

        process.Refresh();
        return process.ExitCode;
    }

    /// <summary>
    /// Manual Win32 process creation, because <see cref="System.Diagnostics.ProcessStartInfo"/> exposes no
    /// property for process-creation flags: it is the only way to give a launched child the
    /// CREATE_NEW_PROCESS_GROUP flag that makes the child's own process id its process group id, which
    /// GenerateConsoleCtrlEvent then requires to target that one child instead of broadcasting to every process
    /// sharing the console the test runner itself owns.
    /// </summary>
    private static class WindowsProcessGroup
    {
        private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint STARTF_USESTDHANDLES = 0x00000100;
        private const uint CTRL_BREAK_EVENT = 1;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static System.Diagnostics.Process Start(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment,
            out TextWriter standardInput,
            out TextReader standardOutput,
            out TextReader standardError)
        {
            var stdinPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var stdoutPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var stderrPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            try
            {
                var startupInfo = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    dwFlags = STARTF_USESTDHANDLES,
                    hStdInput = stdinPipe.ClientSafePipeHandle.DangerousGetHandle(),
                    hStdOutput = stdoutPipe.ClientSafePipeHandle.DangerousGetHandle(),
                    hStdError = stderrPipe.ClientSafePipeHandle.DangerousGetHandle(),
                };

                var commandLine = BuildWindowsCommandLine(executable, arguments);
                var environmentBlock = BuildEnvironmentBlock(environment);
                // Deliberately does not set CREATE_NO_WINDOW: it prevents a managed .NET child from receiving
                // console control events at all (Console.CancelKeyPress never fires), even though an ordinary
                // unmanaged console application still terminates on one by default. Inheriting the parent's
                // existing console instead - while still isolating the child into its own process group via
                // CREATE_NEW_PROCESS_GROUP - keeps CTRL_BREAK_EVENT delivery working without ever opening a new,
                // visible console window.
                var creationFlags = CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT;

                var environmentPointer = Marshal.StringToHGlobalUni(environmentBlock);

                try
                {

                    if (!CreateProcessW(
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            bInheritHandles: true,
                            creationFlags,
                            environmentPointer,
                            workingDirectory,
                            ref startupInfo,
                            out var processInformation))
                    {

                        throw new InvalidOperationException(
                            $"CreateProcess failed for '{executable}' (Win32 error {Marshal.GetLastWin32Error()}).");
                    }

                    NativeMethods.CloseHandle(processInformation.hThread);
                    NativeMethods.CloseHandle(processInformation.hProcess);

                    // The client-side handles were duplicated into the child by inheritance; releasing our own
                    // local copy now is required for standard output/error to observe end-of-file once the child
                    // exits (otherwise this process's own leftover handle would keep the pipe artificially open).
                    stdinPipe.DisposeLocalCopyOfClientHandle();
                    stdoutPipe.DisposeLocalCopyOfClientHandle();
                    stderrPipe.DisposeLocalCopyOfClientHandle();

                    // A Process obtained via GetProcessById is already "associated with a real process", so its
                    // StartInfo cannot be reassigned (the setter throws) - HasExited/ExitCode/Id/Kill/WaitForExit
                    // all still work correctly; only ProcessDiagnostics' command-line summary is unavailable for
                    // a process launched this way, which is an acceptable diagnostics-only trade-off.
                    var process = System.Diagnostics.Process.GetProcessById(processInformation.dwProcessId);

                    standardInput = new StreamWriter(stdinPipe) { AutoFlush = true };
                    standardOutput = new StreamReader(stdoutPipe);
                    standardError = new StreamReader(stderrPipe);
                    return process;
                }
                finally
                {
                    Marshal.FreeHGlobal(environmentPointer);
                }
            }
            catch
            {
                stdinPipe.Dispose();
                stdoutPipe.Dispose();
                stderrPipe.Dispose();
                throw;
            }
        }

        public static void SendCancellationSignal(System.Diagnostics.Process process)
        {
            // CTRL_BREAK_EVENT can target one specific non-zero process group id - unlike CTRL_C_EVENT, which can
            // only ever be broadcast to group 0 (every process sharing the caller's console). Because the child
            // was created with CREATE_NEW_PROCESS_GROUP, its own process id is that group id.

            if (!NativeMethods.GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, (uint)process.Id))
            {
                throw new InvalidOperationException(
                    $"GenerateConsoleCtrlEvent failed for process {process.Id} (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }

        /// <summary>
        /// Reads the exit code for a process adopted through <see cref="System.Diagnostics.Process.GetProcessById(int)"/>
        /// (as returned by <see cref="Start"/>) directly from the operating system by process id, instead of
        /// <see cref="System.Diagnostics.Process.ExitCode"/> - which requires .NET to have already cached its own
        /// process handle internally (something only property accessors like <c>Kill()</c> or <c>SafeHandle</c>
        /// do, never <c>HasExited</c>/<c>WaitForExit</c> alone) and otherwise throws even after the process has
        /// genuinely exited.
        /// </summary>
        public static int GetExitCode(int processId)
        {
            var handle = NativeMethods.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"OpenProcess failed for process {processId} (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            try
            {

                if (!NativeMethods.GetExitCodeProcess(handle, out var exitCode))
                {
                    throw new InvalidOperationException(
                        $"GetExitCodeProcess failed for process {processId} (Win32 error {Marshal.GetLastWin32Error()}).");
                }

                return unchecked((int)exitCode);
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        /// <summary>
        /// Builds one Win32 command-line string following the same quoting rules CommandLineToArgvW (and every
        /// well-behaved Windows CRT) uses to parse it back into an argv array: a backslash run is only escaped
        /// when it precedes a double quote (backslashes must be doubled, and the quote itself escaped), and an
        /// argument is quoted whenever it contains whitespace, a double quote, or is empty.
        /// </summary>
        private static string BuildWindowsCommandLine(string executable, IReadOnlyList<string> arguments)
        {
            var builder = new StringBuilder();
            AppendArgument(builder, executable);

            foreach (var argument in arguments)
            {
                builder.Append(' ');
                AppendArgument(builder, argument);
            }

            return builder.ToString();
        }

        private static void AppendArgument(StringBuilder builder, string argument)
        {
            var needsQuoting = argument.Length == 0 || argument.IndexOfAny([' ', '\t', '"']) >= 0;

            if (!needsQuoting)
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');
            var backslashRun = 0;

            foreach (var c in argument)
            {

                if (c == '\\')
                {
                    backslashRun++;
                    continue;
                }

                if (c == '"')
                {
                    builder.Append('\\', (backslashRun * 2) + 1).Append('"');
                    backslashRun = 0;
                    continue;
                }

                builder.Append('\\', backslashRun).Append(c);
                backslashRun = 0;
            }

            builder.Append('\\', backslashRun * 2).Append('"');
        }

        /// <summary>
        /// Builds a double-null-terminated, null-separated environment block for CreateProcessW. .NET strings are
        /// length-prefixed rather than null-terminated, so the interior null characters this block requires
        /// survive normal string constructions and marshaling without any special pinning.
        /// </summary>
        private static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string?>? overrides)
        {
            var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                variables[(string)entry.Key] = (string?)entry.Value;
            }

            if (overrides is not null)
            {

                foreach (var (name, value) in overrides)
                {
                    variables[name] = value;
                }

            }

            var block = new StringBuilder();

            foreach (var (name, value) in variables.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {

                if (value is null)
                {
                    continue;
                }

                block.Append(name).Append('=').Append(value).Append('\0');
            }

            block.Append('\0');
            return block.ToString();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessW(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
        }
    }

    /// <summary>
    /// Ordinary <see cref="System.Diagnostics.Process.Start()"/> plus a best-effort switch to a brand-new process
    /// group right after creation, so a real POSIX SIGINT can be delivered to that group alone. There is a small,
    /// inherent, well-known race between process creation and the group switch (POSIX offers no portable
    /// .NET-reachable pre-exec hook to set the group atomically at creation time); this is accepted as an
    /// unavoidable limitation of launching through the managed process APIs rather than exec-ing directly.
    /// </summary>
    private static class UnixProcessGroup
    {
        private const int SIGINT = 2;

        public static System.Diagnostics.Process Start(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment,
            out TextWriter standardInput,
            out TextReader standardOutput,
            out TextReader standardError)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (environment is not null)
            {

                foreach (var (name, value) in environment)
                {
                    startInfo.Environment[name] = value;
                }

            }

            var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start '{executable}'.");
            _ = setpgid(process.Id, process.Id);

            standardInput = process.StandardInput;
            standardOutput = process.StandardOutput;
            standardError = process.StandardError;
            return process;
        }

        public static void SendCancellationSignal(System.Diagnostics.Process process)
        {
            // A negative pid targets every process in that process group, matching what a real terminal's Ctrl+C
            // delivers to the whole foreground process group rather than one single process.

            if (kill(-process.Id, SIGINT) != 0)
            {
                throw new InvalidOperationException(
                    $"kill(SIGINT) failed for process group {process.Id} (errno {Marshal.GetLastWin32Error()}).");
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int setpgid(int pid, int pgid);

        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);
    }
}
