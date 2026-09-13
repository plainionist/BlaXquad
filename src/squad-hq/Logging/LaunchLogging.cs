using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using squad.Workspaces;

namespace squadHQ.Logging;

/// <summary>
/// Owns the single backend diagnostic log file for one `squad-hq launch` process lifetime, plus the launch
/// boundary's last-chance handlers for failures that escape the awaited main task. Concrete Serilog wiring stays
/// here, behind <see cref="Microsoft.Extensions.Logging.ILogger"/>, so no other module depends on this particular
/// logging framework.
/// </summary>
sealed class LaunchLogging : IDisposable
{
    private readonly ILoggerFactory myLoggerFactory;
    private readonly UnhandledExceptionEventHandler myUnhandledExceptionHandler;
    private readonly EventHandler<UnobservedTaskExceptionEventArgs> myUnobservedTaskExceptionHandler;
    private bool myDisposed;

    private LaunchLogging(ILoggerFactory loggerFactory, string logFilePath)
    {
        myLoggerFactory = loggerFactory;
        LogFilePath = logFilePath;
        Logger = loggerFactory.CreateLogger("squad-hq.Launch");

        myUnhandledExceptionHandler = (_, eventArgs) =>
            Logger.LogError(
                eventArgs.ExceptionObject as Exception,
                "An unhandled exception escaped the launch process outside the awaited main task.");
        myUnobservedTaskExceptionHandler = (_, eventArgs) =>
        {
            Logger.LogError(eventArgs.Exception, "An unobserved task exception escaped the launch process.");
            eventArgs.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += myUnhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException += myUnobservedTaskExceptionHandler;
    }

    /// <summary>The launch-scoped logger every launch boundary catch clause records failures through.</summary>
    public Microsoft.Extensions.Logging.ILogger Logger { get; }

    /// <summary>The one per-launch log file path, exposed for diagnostics only; nothing but this type writes to it.</summary>
    public string LogFilePath { get; }

    /// <summary>
    /// Creates the one per-launch log file under "&lt;project-root&gt;/.blaxquad/logs/", named from this process's
    /// own UTC start time and process ID, and registers the last-chance handlers for the remainder of the launch.
    /// Call only after the Headquarters lease is held, so a failed competing launch never creates a second file.
    /// </summary>
    public static LaunchLogging Start(ProjectLayout layout)
    {
        var logsDir = Path.Combine(layout.StateDir, "logs");
        Directory.CreateDirectory(logsDir);
        var fileName = $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}Z-{Environment.ProcessId}.log";
        var logFilePath = Path.Combine(logsDir, fileName);

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.File(
                logFilePath,
                // "{UtcTimestamp}" is Serilog's own built-in property for the event's timestamp converted to UTC
                // (see Serilog.Formatting.Display.OutputProperties), so every record is UTC without a custom
                // enricher or relying on the process's local time zone.
                outputTemplate: "{UtcTimestamp:yyyy-MM-ddTHH:mm:ss.fff}Z [{Level}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(serilogLogger, dispose: true);
        return new LaunchLogging(loggerFactory, logFilePath);
    }

    public void Dispose()
    {
        if (myDisposed)
        {
            return;
        }

        myDisposed = true;
        AppDomain.CurrentDomain.UnhandledException -= myUnhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException -= myUnobservedTaskExceptionHandler;
        // Disposing the bridged factory also disposes the underlying Serilog logger (dispose: true above),
        // flushing the file sink before the process exits.
        myLoggerFactory.Dispose();
    }
}
