using squad.Hosting.Abstractions;
using squad.Ui.Abstractions;
using System.Drawing;
using System.Runtime.InteropServices;
using Photino.NET;

namespace squad.Photino;

public sealed class PhotinoWindowHost : IWindowHost
{
    private const int mySilentLogVerbosity = 0;
    private const int myUseImmersiveDarkMode = 20;
    private const int myUseImmersiveDarkModeBeforeWindows10_2004 = 19;
    private const int myCaptionColor = 35;
    private const int myWindowBackgroundColor = 0x1F1F1F;
    private static readonly TimeSpan myUiReadyTimeout = TimeSpan.FromSeconds(30);
    private readonly UiProtocolSession mySession;
    private readonly string myUiDirectory;
    private readonly string myTitle;
    private readonly Action<string>? mySerializedMessageSink;
    private readonly TaskCompletionSource myClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myUiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myWindowCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object myStopLock = new();
    private PhotinoWindow? myWindow;
    private Task? myStop;
    private Thread? myUiThread;
    private bool myStarted;

    public PhotinoWindowHost(
        ISquadUi ui,
        string workspaceDirectory,
        string? uiDirectory = null,
        Action<string>? openExternalUrl = null)
        : this(
            ui,
            workspaceDirectory,
            uiDirectory,
            openExternalUrl,
            serializedMessageSink: null)
    {
    }

    public PhotinoWindowHost(
        ISquadUi ui,
        string workspaceDirectory,
        string? uiDirectory,
        Action<string>? openExternalUrl,
        Action<string>? serializedMessageSink)
    {
        myUiDirectory = uiDirectory ?? Path.Combine(AppContext.BaseDirectory, "ui");
        myTitle = CreateTitle(workspaceDirectory);
        mySerializedMessageSink = serializedMessageSink;
        mySession = new(
            ui,
            SendSerializedMessage,
            () => myUiReady.TrySetResult(),
            () => _ = StopAsync(),
            openExternalUrl);
    }

    public static string CreateTitle(string workspaceDirectory) =>
        $"BlaXquad - {Path.GetFullPath(workspaceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}";

    public bool HasCloseSignal => myClosed.Task.IsCompleted;
    public Task UiReady => myUiReady.Task;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (myStarted)
            return Task.CompletedTask;

        EnsureLinuxDisplayIsAvailable();
        var index = Path.Combine(myUiDirectory, "index.html");
        if (!File.Exists(index))
            throw new InvalidOperationException($"Photino UI was not found at '{index}'. Build src/squad-ui before launching the Photino host.");
        myStarted = true;
        myUiThread = new Thread(() => RunWindow(index))
        {
            IsBackground = true,
            Name = "BlaXquad Photino UI",
        };
        if (OperatingSystem.IsWindows())
            myUiThread.SetApartmentState(ApartmentState.STA);
        myUiThread.Start();
        return WaitForUiReadyAsync(cancellationToken);
    }

    public Task SessionsStartedAsync(
        CancellationToken cancellationToken = default) =>
        mySession.SessionsStartedAsync(cancellationToken);

    private void RunWindow(string index)
    {
        try
        {
            var window = new PhotinoWindow();
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "BlaXquad.ico");
            window
                .SetLogVerbosity(mySilentLogVerbosity)
                .SetTitle(myTitle)
                .SetIconFile(icon)
                .SetUseOsDefaultSize(false)
                .SetSize(new Size(1440, 900))
                .Center()
                .RegisterWindowCreatedHandler((sender, eventArgs) => EnableWindowsDarkTitleBar(window))
                .RegisterWindowClosingHandler((sender, eventArgs) => myClosed.TrySetResult())
                .RegisterWebMessageReceivedHandler((sender, message) => _ = ReceiveMessageAsync(message))
                .Load(index);
            myWindow = window;
            myWindowCreated.TrySetResult();
            mySession.AttachUiEventSources();
            myWindow.WaitForClose();
            myClosed.TrySetResult();
        }
        catch (Exception exception)
        {
            myWindowCreated.TrySetException(exception);
            myClosed.TrySetException(exception);
            myUiReady.TrySetException(exception);
        }
        finally
        {
            mySession.DetachUiEventSources();
        }
    }

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default)
    {
        if (myWindow is null)
            throw new InvalidOperationException("Photino window has not been started.");
        return myClosed.Task.WaitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (myStopLock)
            return myStop ??= StopCoreAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        mySession.DetachUiEventSources();
        myWindow = null;
    }

    private async Task StopCoreAsync()
    {
        mySession.DetachUiEventSources();
        try
        {
            await mySession.DisposeAsync();
        }
        finally
        {
            if (!myStarted)
            {
                myClosed.TrySetResult();
                myUiReady.TrySetCanceled();
            }
            else
            {
                if (!myClosed.Task.IsCompleted)
                {
                    await myWindowCreated.Task;
                    myWindow!.Close();
                }
                await myClosed.Task;
                myUiReady.TrySetCanceled();
            }
        }
    }

    private async Task WaitForUiReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await myUiReady.Task.WaitAsync(myUiReadyTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("The Photino UI did not become ready within 30 seconds.");
        }
    }

    private static void EnsureLinuxDisplayIsAvailable()
    {
        if (OperatingSystem.IsLinux() &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            throw new InvalidOperationException("Photino requires a graphical display. Launch from a session with DISPLAY or WAYLAND_DISPLAY configured.");
    }

    private static void EnableWindowsDarkTitleBar(PhotinoWindow window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var enabled = 1;
        if (DwmSetWindowAttribute(window.WindowHandle, myUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(window.WindowHandle, myUseImmersiveDarkModeBeforeWindows10_2004, ref enabled, sizeof(int));
        var backgroundColor = myWindowBackgroundColor;
        _ = DwmSetWindowAttribute(window.WindowHandle, myCaptionColor, ref backgroundColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

    public Task ReceiveMessageAsync(string message) =>
        mySession.ReceiveMessageAsync(message);

    private void SendSerializedMessage(string message)
    {
        if (mySerializedMessageSink is not null)
        {
            mySerializedMessageSink(message);
            return;
        }
        myWindow?.SendWebMessage(message);
    }
}



