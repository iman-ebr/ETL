using Serilog;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace Mapna.Sender.Logging;

public class LoggingSetup
{
    public const string ConnectivityLost = "ConnectivityLost";
    public const string ConnectivityRestored = "ConnectivityRestored";
    public const string CircuitOpened = "CircuitOpened";
    public const string CircuitClosed = "CircuitClosed";
    public const string RunStarted = "RunStarted";
    public const string RunResumed = "RunResumed";
    public const string RunCompleted = "RunCompleted";
    public const string RunInterrupted = "RunInterrupted";
    public const string StageCompleted = "StageCompleted";
    public const string SourceReadFailed = "SourceReadFailed";
    public const string ConcurrentRunBlocked = "ConcurrentRunBlocked";
    public const string ItemSendFailed = "ItemSendFailed";


    public static ILogger CreateLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        const string fileTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] ({RunId}) {EventType} :: {Message:lj} {NewLine}{Exception}";

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "Mapna.Sender")
            .WriteTo.File(
                path: Path.Combine(logsDirectory, "sender-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: fileTemplate,
                shared: true)
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {EventType} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}