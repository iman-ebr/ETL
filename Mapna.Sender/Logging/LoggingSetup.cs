using Serilog;
using Serilog.Context;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace Mapna.Sender.Logging;

public static class LoggingSetup
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

    public static IDisposable BeginRunScope(this ILogger _, Guid runId) =>
        LogContext.PushProperty("RunId", runId);

    public static void LogRunStarted(this ILogger logger, Guid runId, int totalCount) =>
        logger.ForContext("EventType", RunStarted)
              .Information("New sync run started with {TotalCount} record(s) loaded and staged", totalCount);

    public static void LogStageCompleted(this ILogger logger, Guid runId, int count, TimeSpan elapsed) =>
        logger.ForContext("EventType", StageCompleted)
              .Information("Staged {Count} record(s) as Pending in {ElapsedMs}ms (durable in AppDatabase before any network call)",
                  count, elapsed.TotalMilliseconds);

    public static void LogRunResumed(this ILogger logger, Guid runId, int totalCount, int pendingCount) =>
        logger.ForContext("EventType", RunResumed)
              .Warning(
                  "Resuming previous run: {PendingCount} of {TotalCount} record(s) are still Pending and will be " +
                  "re-validated against the source database; the rest already finished and will be skipped",
                  pendingCount, totalCount);

    public static void LogRunCompleted(this ILogger logger, Guid runId, int sent, int duplicate, int failed, TimeSpan elapsed) =>
        logger.ForContext("EventType", RunCompleted)
              .Information("Run finished in {ElapsedSeconds:0.0}s — Sent={Sent}, Duplicate={Duplicate}, Failed={Failed}",
                  elapsed.TotalSeconds, sent, duplicate, failed);

    public static void LogRunInterrupted(this ILogger logger, Guid runId, string reason, int processed, int total) =>
        logger.ForContext("EventType", RunInterrupted)
              .Warning("Run stopped after {Processed}/{Total} record(s). Reason: {Reason}. It can be resumed on next start.",
                  processed, total, reason);

    public static void LogConnectivityLost(this ILogger logger, Guid runId, string source, int remainingCount, Exception? ex) =>
        logger.ForContext("EventType", ConnectivityLost)
              .Error(ex,
                  "Connectivity lost ({Source}) after repeated failures. {RemainingCount} record(s) still pending; " +
                  "processing is paused and will resume automatically once the connection is back.",
                  source, remainingCount);

    public static void LogConnectivityRestored(this ILogger logger, Guid runId, string source, TimeSpan downtime) =>
        logger.ForContext("EventType", ConnectivityRestored)
              .Information("Connectivity restored ({Source}) after {DowntimeSeconds:0.0}s. Resuming processing.",
                  source, downtime.TotalSeconds);

    public static void LogSourceReadFailed(this ILogger logger, Exception ex) =>
        logger.ForContext("EventType", SourceReadFailed)
              .Error(ex, "Failed to read personnel records from the source database after retrying. No records were processed this run.");

    public static void LogConcurrentRunBlocked(this ILogger logger, Guid activeRunId) =>
        logger.ForContext("EventType", ConcurrentRunBlocked)
              .Warning("Refusing to start: run {ActiveRunId} appears to be actively running elsewhere (recent heartbeat)", activeRunId);

    public static void LogItemSendFailed(this ILogger logger, Guid runId, int perId, string reason) =>
        logger.ForContext("EventType", ItemSendFailed)
              .Warning("PerId {PerId} failed: {Reason}", perId, reason);
}