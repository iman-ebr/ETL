using Mapna.Contracts;
using Mapna.LogData;
using Mapna.Sender.Logging;
using Mapna.Sender.Staging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Serilog;
using System.Diagnostics;

namespace Mapna.Sender;

public sealed class ConcurrentRunDetectedException(Guid activeRunId)
    : Exception($"یک اجرای دیگر هم‌اکنون در حال انجام است (شناسه: {activeRunId}). لطفاً صبر کنید تا تمام شود.")
{
    public Guid ActiveRunId { get; } = activeRunId;
}

public class SyncOrchestrator
{
    private readonly AppSettings _settings;
    private readonly SqlStagingRepository _staging;
    private readonly ILogger _logger;

    private const string ReceiverApiClientName = "ReceiverApi";
    private const int FlushBatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CircuitBreakDuration = SqlStagingRepository.CircuitBreakDuration;
    private const int CircuitBreakThreshold = 5;

    public SyncOrchestrator(AppSettings settings, SqlStagingRepository staging, ILogger logger)
    {
        _settings = settings;
        _staging = staging;
        _logger = logger;
    }

    public async Task RunAsync(IProgress<SyncProgress> progress, CancellationToken cancellationToken, Guid? resumeRunId = null)
    {
        var activeElsewhere = await _staging.FindActiveRunAsync(cancellationToken);
        if (activeElsewhere is not null && activeElsewhere.RunId != resumeRunId)
        {
            _logger.LogConcurrentRunBlocked(activeElsewhere.RunId);
            throw new ConcurrentRunDetectedException(activeElsewhere.RunId);
        }

        // ---- Load: pause/retry indefinitely on a sustained outage ----
        var srcRepo = new SourceRepository(_settings.SourceConnectionString);
        IReadOnlyList<PersonnelRecord> records;
        while (true)
        {
            try
            {
                    records = await srcRepo.GetAllPersonnelAsync();
                break;
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogConnectivityLost(Guid.Empty, "Source Database (Load)", 0, ex);
                progress.Report(new SyncProgress
                {
                    IsPaused = true,
                    PauseMessage = $"اتصال به پایگاه‌داده مبدأ در زمان بارگذاری قطع شده — تلاش مجدد خودکار هر {CircuitBreakDuration.TotalSeconds:0} ثانیه..."
                });

                var downtime = Stopwatch.StartNew();
                await Task.Delay(CircuitBreakDuration + TimeSpan.FromSeconds(2), cancellationToken);
                _logger.LogConnectivityRestored(Guid.Empty, "Source Database (Load)", downtime.Elapsed);
            }
            catch (Exception ex)
            {
                _logger.LogSourceReadFailed(ex);
                throw;
            }
        }

        var recordsByPerId = records.ToDictionary(r => r.PerId);
        var totalCount = records.Count;

        await using var logdb = LogsDbContextFactory.Create(_settings.AppConnectionString);
        var lastSentByPerId = await SendDecisionService.LoadLastSentAsync(logdb, cancellationToken);
        var decisionService = new SendDecisionService(lastSentByPerId);

        // ---- Stage ----
        Guid runId;
        HashSet<int> toProcessPerIds;
        int preFinishedCount;

        if (resumeRunId is { } existingId && await _staging.GetRunAsync(existingId, cancellationToken) is { } existingRun)
        {
            runId = existingId;
            var pendingIds = await _staging.GetPendingPerIdsAsync(runId, cancellationToken);
            toProcessPerIds = pendingIds.Intersect(recordsByPerId.Keys).ToHashSet();
            preFinishedCount = existingRun.ProcessedCount;
            _logger.LogRunResumed(runId, totalCount, toProcessPerIds.Count);
        }
        else
        {
            var run = await _staging.StartRunAsync(totalCount, cancellationToken);
            runId = run.RunId;
            preFinishedCount = 0;

            var stageWatch = Stopwatch.StartNew();
            var stageItems = records.Select(r => (r.PerId, PersonName: $"{r.PerName} {r.PerSurname}".Trim())).ToList();
            try
            {
                await _staging.StageBatchAsync(runId, stageItems, cancellationToken);
            }
            catch (Exception ex)
            {
                var closedAs = ex is OperationCanceledException ? RunStatus.Cancelled : RunStatus.Crashed;
                try { await _staging.CompleteRunAsync(runId, closedAs, LogFieldLimit.Truncate($"Staging failed: {ex.Message}", 450), CancellationToken.None); }
                catch (Exception completeEx) { _logger.Warning(completeEx, "Could not close run {RunId} after a failed staging step", runId); }
                throw;
            }
            _logger.LogStageCompleted(runId, stageItems.Count, stageWatch.Elapsed);

            toProcessPerIds = recordsByPerId.Keys.ToHashSet();
            _logger.LogRunStarted(runId, totalCount);
        }

        using var runScope = _logger.BeginRunScope(runId);
        var efResiliencePolicy = BuildEfResiliencePolicy(runId);

        using var serviceProvider = BuildHttpServices(runId);
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var httpclient = httpClientFactory.CreateClient(ReceiverApiClientName);
        httpclient.BaseAddress = new Uri(_settings.ReceiverApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(_settings.ReceiverApiKey))
            httpclient.DefaultRequestHeaders.Add("X-Api-Key", _settings.ReceiverApiKey);

        var sender = new RecordSender(httpclient, logdb);

        int processedCount = 0, sentCount = 0, duplicateCount = 0, failedCount = 0;
        var flushBuffer = new List<StagingItem>();
        var flushStopwatch = Stopwatch.StartNew();

        string? stopReason = null;
        var wasCancelled = false;
        var runStopwatch = Stopwatch.StartNew();

        try
        {
            foreach (var perId in toProcessPerIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = recordsByPerId[perId];

                SendOutcome outcome;
                while (true)
                {
                    try
                    {
                        var decision = decisionService.Decide(record);
                        outcome = await sender.SendAsync(record, decision, cancellationToken);
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (BrokenCircuitException ex)
                    {
                        var remaining = toProcessPerIds.Count - processedCount;
                        _logger.LogConnectivityLost(runId, "Receiver API", remaining, ex);
                        progress.Report(BuildProgress(totalCount, preFinishedCount + processedCount, sentCount, duplicateCount, failedCount,
                            record, true, $"اتصال به سرور دریافت‌کننده قطع شده — تلاش مجدد خودکار هر {CircuitBreakDuration.TotalSeconds:0} ثانیه...", runId));

                        await TryHeartbeatAsync(runId, cancellationToken);
                        var downtime = Stopwatch.StartNew();
                        await Task.Delay(CircuitBreakDuration + TimeSpan.FromSeconds(2), cancellationToken);
                        _logger.LogConnectivityRestored(runId, "Receiver API", downtime.Elapsed);

                        progress.Report(BuildProgress(totalCount, preFinishedCount + processedCount, sentCount, duplicateCount, failedCount,
                            record, false, null, runId));
                    }
                    catch (Exception ex)
                    {
                        outcome = new SendOutcome(SendStatus.SendFailed, $"Unexpected error: {ex.Message}", null, decisionService.Decide(record).PayloadSnapshot);
                        break;
                    }
                }

                processedCount++;
                switch (outcome.Status)
                {
                    case SendStatus.Sent: sentCount++; break;
                    case SendStatus.Duplicate: duplicateCount++; break;
                    default: failedCount++; break;
                }

                var personName = $"{record.PerName} {record.PerSurname}".Trim();
                flushBuffer.Add(new StagingItem
                {
                    RunId = runId,
                    PerId = perId,
                    PersonName = personName,
                    Status = StagingStatusMapper.FromSendStatus(outcome.Status),
                    Reason = outcome.Reason,
                    ChangedFields = outcome.ChangedFields,
                    PayloadSnapshot = outcome.PayloadSnapshot
                });

                var shouldFlush = flushBuffer.Count >= FlushBatchSize || flushStopwatch.Elapsed >= FlushInterval;
                if (shouldFlush)
                {
                    await FlushWithConnectivityPauseAsync(runId, flushBuffer, logdb, efResiliencePolicy, progress, totalCount, preFinishedCount, processedCount,
                        sentCount, duplicateCount, failedCount, record, cancellationToken);
                    flushBuffer.Clear();
                    flushStopwatch.Restart();
                }

                progress.Report(BuildProgress(totalCount, preFinishedCount + processedCount, sentCount, duplicateCount, failedCount,
                    record, false, null, runId, outcome.Status, outcome.Reason));
            }
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            stopReason = "لغو شده توسط کاربر";
            throw;
        }
        catch (Exception ex)
        {
            stopReason = $"خطای غیرمنتظره: {ex.Message}";
            throw;
        }
        finally
        {
            if (logdb.ChangeTracker.HasChanges())
            {
                try { await efResiliencePolicy.ExecuteAsync(ct => logdb.SaveChangesAsync(ct), CancellationToken.None); }
                catch (Exception ex) { _logger.Warning(ex, "Final SendLogs flush failed for run {RunId} - the staging items below are left Pending so they self-heal on next resume", runId); }
            }

            if (flushBuffer.Count > 0)
            {
                try
                {
                    await _staging.FlushResultsAsync(runId, flushBuffer, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Final staging flush failed for run {RunId} - {Count} result(s) remain Pending and will be re-validated on next resume",
                        runId, flushBuffer.Count);
                }
            }

            var finalStatus = wasCancelled
                ? RunStatus.Cancelled
                : stopReason is not null
                    ? RunStatus.Paused
                    : failedCount > 0 ? RunStatus.CompletedWithFailures : RunStatus.Completed;

            try { await _staging.CompleteRunAsync(runId, finalStatus, stopReason, CancellationToken.None); }
            catch (Exception ex) { _logger.Warning(ex, "Could not mark run {RunId} as finished ({Status}) - it may show as resumable next time", runId, finalStatus); }

            if (stopReason is not null)
                _logger.LogRunInterrupted(runId, stopReason, preFinishedCount + processedCount, totalCount);
            else
                _logger.LogRunCompleted(runId, sentCount, duplicateCount, failedCount, runStopwatch.Elapsed);
        }
    }

    private async Task FlushWithConnectivityPauseAsync(Guid runId, List<StagingItem> batch, LogDbContext logdb, IAsyncPolicy efResiliencePolicy,
        IProgress<SyncProgress> progress, int total, int preFinished, int processed, int sent, int duplicate, int failed,
        PersonnelRecord current, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                if (logdb.ChangeTracker.HasChanges())
                {
                    await efResiliencePolicy.ExecuteAsync(ct => logdb.SaveChangesAsync(ct), cancellationToken);
                    logdb.ChangeTracker.Clear();
                }

                await _staging.FlushResultsAsync(runId, batch, cancellationToken);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (BrokenCircuitException ex)
            {
                _logger.LogConnectivityLost(runId, "AppDatabase", batch.Count, ex);
                progress.Report(BuildProgress(total, preFinished + processed, sent, duplicate, failed, current,
                    true, $"اتصال به پایگاه‌داده قطع شده — تلاش مجدد خودکار هر {CircuitBreakDuration.TotalSeconds:0} ثانیه...", runId));

                var downtime = Stopwatch.StartNew();
                await Task.Delay(CircuitBreakDuration + TimeSpan.FromSeconds(2), cancellationToken);
                _logger.LogConnectivityRestored(runId, "AppDatabase", downtime.Elapsed);

                progress.Report(BuildProgress(total, preFinished + processed, sent, duplicate, failed, current,
                    false, null, runId));
            }
        }
    }

    private async Task TryHeartbeatAsync(Guid runId, CancellationToken cancellationToken)
    {
        try { await _staging.HeartbeatAsync(runId, cancellationToken); }
        catch { }
    }

    private static SyncProgress BuildProgress(int total, int processed, int sent, int duplicate, int failed,
        PersonnelRecord current, bool isPaused, string? pauseMessage, Guid runId,
        SendStatus? lastStatus = null, string? lastReason = null) => new()
        {
            Total = total,
            Processed = processed,
            SentCount = sent,
            DuplicateCount = duplicate,
            FailedCount = failed,
            CurrentPerson = $"{current.PerName} {current.PerSurname}",
            CurrentPerId = current.PerId,
            LastStatus = lastStatus ?? default,
            LastReason = lastReason,
            IsPaused = isPaused,
            PauseMessage = pauseMessage,
            RunId = runId
        };

    private IAsyncPolicy BuildEfResiliencePolicy(Guid runId)
    {
        var retry = Policy
            .Handle<DbUpdateException>()
            .Or<SqlException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(retryCount: 3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreaker = Policy
            .Handle<DbUpdateException>()
            .Or<SqlException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: CircuitBreakThreshold,
                durationOfBreak: CircuitBreakDuration,
                onBreak: (ex, breakDuration) =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitOpened).ForContext("RunId", runId)
                        .Warning(ex, "SendLogs (EF) circuit opened for {BreakSeconds:0}s after {Threshold} consecutive failures (AppDatabase)",
                            breakDuration.TotalSeconds, CircuitBreakThreshold),
                onReset: () =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitClosed).ForContext("RunId", runId)
                        .Information("SendLogs (EF) circuit closed - AppDatabase is reachable again"),
                onHalfOpen: () => _logger.ForContext("RunId", runId).Debug("SendLogs (EF) circuit half-open - probing AppDatabase"));

        return Policy.WrapAsync(retry, circuitBreaker);
    }

    private ServiceProvider BuildHttpServices(Guid runId)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(ReceiverApiClientName).AddPolicyHandler(GetResiliencePolicy(runId));
        return services.BuildServiceProvider();
    }

    private IAsyncPolicy<HttpResponseMessage> GetResiliencePolicy(Guid runId)
    {
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(retryCount: 3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: CircuitBreakThreshold,
                durationOfBreak: CircuitBreakDuration,
                onBreak: (_, breakDuration) =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitOpened).ForContext("RunId", runId)
                        .Warning("HTTP circuit opened for {BreakSeconds:0}s after {Threshold} consecutive failures (Receiver API)",
                            breakDuration.TotalSeconds, CircuitBreakThreshold),
                onReset: () =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitClosed).ForContext("RunId", runId)
                        .Information("HTTP circuit closed - Receiver API is reachable again"),
                onHalfOpen: () => _logger.ForContext("RunId", runId).Debug("HTTP circuit half-open - probing Receiver API"));

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }
}