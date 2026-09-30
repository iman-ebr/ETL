using System.Diagnostics;
using Mapna.Contracts;
using Mapna.LogData;
using Mapna.Sender.Logging;
using Mapna.Sender.Staging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Serilog;

namespace Mapna.Sender;

public sealed class ConcurrentRunDetectedException(Guid activeRunId)
    : Exception(activeRunId == Guid.Empty
        ? "یک اجرای دیگر هم‌اکنون در حال انجام است. لطفاً صبر کنید تا تمام شود."
        : $"یک اجرای دیگر هم‌اکنون در حال انجام است (شناسه: {activeRunId}). لطفاً صبر کنید تا تمام شود.")
{
    public Guid ActiveRunId { get; } = activeRunId;
}

public sealed class SyncConfigurationException(IReadOnlyList<string> errors)
    : Exception("پیکربندی نامعتبر است:\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class SchemaOutOfDateException(IReadOnlyList<string> pendingMigrations)
    : Exception("ساختار پایگاه‌داده به‌روز نیست. Migrationهای معوق: " + string.Join(", ", pendingMigrations))
{
    public IReadOnlyList<string> PendingMigrations { get; } = pendingMigrations;
}

public class SyncOrchestrator
{
    private readonly AppSettings _settings;
    private readonly SqlStagingRepository _staging;
    private readonly ILogger _logger;

    // Small batches: a crash loses at most this many *audit rows*. Never data: those records stay Pending and are
    // resent on resume, where the idempotent receiver answers "Duplicate".
    private const int FlushBatchSize = 25;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PausedHeartbeatInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectivityWait = ReceiverHttpPipeline.BreakDuration + TimeSpan.FromSeconds(2);
    private const int MaxTransientRoundsPerRecord = 4;

    public SyncOrchestrator(AppSettings settings, SqlStagingRepository staging, ILogger logger)
    {
        _settings = settings;
        _staging = staging;
        _logger = logger;
    }

    /// <summary>Test seam: lets integration tests route HTTP to an in-process receiver (WebApplicationFactory).</summary>
    internal Func<HttpMessageHandler>? PrimaryHttpHandler { get; init; }

    public async Task RunAsync(IProgress<SyncProgress> progress, CancellationToken cancellationToken, Guid? resumeRunId = null, PauseToken pauseToken = default)
    {
        var configErrors = _settings.Validate();
        if (configErrors.Count > 0)
            throw new SyncConfigurationException(configErrors);

        progress.Report(new SyncProgress { Phase = SyncPhase.Preparing });

        await using var runLock = await SyncRunLock.TryAcquireAsync(_settings.AppConnectionString, cancellationToken);
        if (runLock is null)
        {
            var active = await _staging.FindActiveRunAsync(cancellationToken);
            _logger.LogConcurrentRunBlocked(active?.RunId ?? Guid.Empty);
            throw new ConcurrentRunDetectedException(active?.RunId ?? Guid.Empty);
        }

        await using var logdb = LogsDbContextFactory.Create(_settings.AppConnectionString);
        var pending = (await logdb.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
            throw new SchemaOutOfDateException(pending);
        await _staging.EnsureSchemaAsync(cancellationToken);

        progress.Report(new SyncProgress { Phase = SyncPhase.LoadingSource });
        var source = SourceSnapshot.Create(await LoadSourceAsync(progress, cancellationToken));
        var decisionService = new SendDecisionService(await SendDecisionService.LoadStatesAsync(logdb, cancellationToken));

        progress.Report(new SyncProgress { Phase = SyncPhase.Staging, Total = source.Count });
        var run = await PrepareRunAsync(resumeRunId, source, cancellationToken);
        var ctx = new RunContext(run.RunId, run.Total, run.Baseline, progress, runLock);

        using var runScope = _logger.BeginRunScope(ctx.RunId);
        using var httpServices = ReceiverHttpPipeline.Build(_settings, _logger, ctx.RunId, PrimaryHttpHandler);
        var httpClient = httpServices.GetRequiredService<IHttpClientFactory>().CreateClient(ReceiverHttpPipeline.ClientName);
        var sender = new RecordSender(httpClient);

        var buffer = new List<SyncItemResult>();
        var flushWatch = Stopwatch.StartNew();
        var runWatch = Stopwatch.StartNew();
        RunStatus finalStatus = RunStatus.Completed;
        string? stopReason = null;

        try
        {
            foreach (var perId in run.ToProcess)
            {
                if (pauseToken.IsPaused)
                {
                    await FlushAsync(ctx, buffer, cancellationToken);
                    flushWatch.Restart();
                    await HoldWhilePausedAsync(ctx, pauseToken, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();

                var name = source.Names.GetValueOrDefault(perId, string.Empty);
                ctx.CurrentPerId = perId;
                ctx.CurrentName = name;

                SyncItemResult result;
                if (source.DuplicatePerIds.TryGetValue(perId, out var occurrences))
                {
                    result = new SyncItemResult
                    {
                        PerId = perId, PersonName = name, Status = SendStatus.ValidationFailed,
                        Reason = $"PerId {perId} در جدول مبدأ {occurrences} بار تکرار شده است؛ برای جلوگیری از بازنویسی اشتباه ارسال نشد."
                    };
                }
                else if (!source.ByPerId.TryGetValue(perId, out var record))
                {
                    result = new SyncItemResult
                    {
                        PerId = perId, PersonName = name, Status = SendStatus.SendFailed,
                        Reason = "این رکورد پس از شروع اجرا از جدول مبدأ حذف شده است؛ چیزی ارسال نشد."
                    };
                }
                else
                {
                    var decision = decisionService.Decide(record);
                    var outcome = await SendWithRecoveryAsync(ctx, sender, record, decision, cancellationToken);
                    result = new SyncItemResult
                    {
                        PerId = perId,
                        PersonName = name,
                        Status = outcome.Status,
                        Reason = outcome.Reason,
                        ChangedFields = outcome.ChangedFields,
                        PayloadSnapshot = outcome.PayloadSnapshot,
                        ConfirmedByReceiver = outcome.ConfirmedByReceiver,
                        CorrelationId = outcome.CorrelationId ?? Guid.NewGuid()
                    };
                    if (outcome.Status == SendStatus.SendFailed)
                        _logger.LogItemSendFailed(ctx.RunId, perId, outcome.Reason ?? "");
                }

                buffer.Add(result);
                ctx.Count(result.Status);

                if (buffer.Count >= FlushBatchSize || flushWatch.Elapsed >= FlushInterval)
                {
                    await FlushAsync(ctx, buffer, cancellationToken);
                    flushWatch.Restart();
                }

                progress.Report(ctx.Snapshot(result));
            }

            await FlushAsync(ctx, buffer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            finalStatus = RunStatus.Paused;
            stopReason = "توقف توسط کاربر";
            throw;
        }
        catch (Exception ex)
        {
            finalStatus = RunStatus.Paused;
            stopReason = $"{ex.GetType().Name}: {ex.Message}";
            throw;
        }
        finally
        {
            progress.Report(new SyncProgress { Phase = SyncPhase.Finalizing, RunId = ctx.RunId, Total = ctx.Total });

            if (buffer.Count > 0)
            {
                try { await _staging.FlushResultsAsync(ctx.RunId, buffer, CancellationToken.None); }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Final flush failed for run {RunId}; {Count} result(s) remain Pending and will be re-evaluated on resume",
                        ctx.RunId, buffer.Count);
                }
            }

            try { await _staging.CompleteRunAsync(ctx.RunId, finalStatus, stopReason, CancellationToken.None); }
            catch (Exception ex) { _logger.Warning(ex, "Could not mark run {RunId} as {Status}; it will be offered for resume", ctx.RunId, finalStatus); }

            if (stopReason is not null)
                _logger.LogRunInterrupted(ctx.RunId, stopReason, ctx.ProcessedTotal, ctx.Total);
            else
                _logger.LogRunCompleted(ctx.RunId, ctx.Sent, ctx.Duplicate, ctx.Failed, runWatch.Elapsed);
        }
    }

    private async Task<IReadOnlyList<PersonnelRecord>> LoadSourceAsync(IProgress<SyncProgress> progress, CancellationToken cancellationToken)
    {
        var repository = new SourceRepository(_settings.SourceConnectionString, _settings.NormalizeArabicLetters);
        while (true)
        {
            try
            {
                return await repository.GetAllPersonnelAsync(cancellationToken);
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogConnectivityLost(Guid.Empty, "Source Database (Load)", 0, ex);
                progress.Report(new SyncProgress
                {
                    Phase = SyncPhase.LoadingSource,
                    IsPaused = true,
                    PauseKind = SyncPauseKind.Connectivity,
                    PauseMessage = "اتصال به پایگاه‌داده مبدأ برقرار نیست — تلاش مجدد خودکار..."
                });
                var downtime = Stopwatch.StartNew();
                await Task.Delay(SqlStagingRepository.CircuitBreakDuration + TimeSpan.FromSeconds(2), cancellationToken);
                _logger.LogConnectivityRestored(Guid.Empty, "Source Database (Load)", downtime.Elapsed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogSourceReadFailed(ex);
                throw;
            }
        }
    }

    private sealed record PreparedRun(Guid RunId, int Total, IReadOnlyList<int> ToProcess, RunBaseline Baseline);

    private async Task<PreparedRun> PrepareRunAsync(Guid? resumeRunId, SourceSnapshot source, CancellationToken cancellationToken)
    {
        var stageItems = source.AllPerIds.Select(id => (id, source.Names.GetValueOrDefault(id, ""))).ToList();

        if (resumeRunId is { } existingId &&
            await _staging.GetRunAsync(existingId, cancellationToken) is { Status: RunStatus.Paused or RunStatus.Running or RunStatus.Crashed })
        {
            var added = await _staging.StageBatchAsync(existingId, stageItems, cancellationToken);
            await _staging.MarkRunResumedAsync(existingId, source.Count, cancellationToken);
            var run = await _staging.GetRunAsync(existingId, cancellationToken) ?? throw new InvalidOperationException("Run vanished");
            var pendingIds = await _staging.GetPendingPerIdsAsync(existingId, cancellationToken);
            _logger.LogRunResumed(existingId, run.TotalCount, pendingIds.Count);
            if (added > 0)
                _logger.Information("Resume staged {Added} PerId(s) that appeared in the source after the run started", added);

            return new PreparedRun(existingId, run.TotalCount, pendingIds.Order().ToList(),
                new RunBaseline(run.ProcessedCount, run.SentCount, run.DuplicateCount, run.FailedCount));
        }

        var newRun = await _staging.StartRunAsync(source.Count, cancellationToken);
        var stageWatch = Stopwatch.StartNew();
        try
        {
            await _staging.StageBatchAsync(newRun.RunId, stageItems, cancellationToken);
        }
        catch (Exception ex)
        {
            var closedAs = ex is OperationCanceledException ? RunStatus.Cancelled : RunStatus.Crashed;
            try { await _staging.CompleteRunAsync(newRun.RunId, closedAs, $"Staging failed: {ex.Message}", CancellationToken.None); }
            catch (Exception completeEx) { _logger.Warning(completeEx, "Could not close run {RunId} after a failed staging step", newRun.RunId); }
            throw;
        }
        _logger.LogStageCompleted(newRun.RunId, stageItems.Count, stageWatch.Elapsed);
        _logger.LogRunStarted(newRun.RunId, source.Count);

        return new PreparedRun(newRun.RunId, source.Count, source.AllPerIds.Order().ToList(), new RunBaseline(0, 0, 0, 0));
    }

    private async Task<SendOutcome> SendWithRecoveryAsync(RunContext ctx, RecordSender sender, PersonnelRecord record, SendDecision decision,
        CancellationToken cancellationToken)
    {
        var transientRounds = 0;
        while (true)
        {
            try
            {
                return await sender.SendAsync(record, decision, cancellationToken);
            }
            catch (BrokenCircuitException ex)
            {
                await WaitForConnectivityAsync(ctx, "Receiver API", ex, cancellationToken);
            }
            catch (TransientSendException ex)
            {
                transientRounds++;
                if (transientRounds >= MaxTransientRoundsPerRecord)
                {
                    return new SendOutcome(SendStatus.SendFailed,
                        $"پس از {transientRounds} دور تلاش ناموفق ماند: {ex.Message}", null, decision.PayloadSnapshot);
                }

                var wait = TimeSpan.FromSeconds(10 * transientRounds);
                _logger.Warning(ex, "Transient failure for PerId {PerId} (round {Round}/{Max}); retrying in {Wait}s",
                    record.PerId, transientRounds, MaxTransientRoundsPerRecord, wait.TotalSeconds);
                ctx.Progress.Report(ctx.Paused(SyncPauseKind.Throttled,
                    $"سرور دریافت‌کننده موقتاً پاسخ نمی‌دهد — تلاش مجدد {transientRounds}/{MaxTransientRoundsPerRecord - 1} پس از {wait.TotalSeconds:0} ثانیه..."));
                await Task.Delay(wait, cancellationToken);
                ctx.Progress.Report(ctx.Resumed());
            }
        }
    }

    private async Task FlushAsync(RunContext ctx, List<SyncItemResult> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0) return;

        await ctx.RunLock.EnsureHeldAsync(cancellationToken);

        while (true)
        {
            try
            {
                await _staging.FlushResultsAsync(ctx.RunId, buffer, cancellationToken);
                buffer.Clear();
                return;
            }
            catch (BrokenCircuitException ex)
            {
                await WaitForConnectivityAsync(ctx, "AppDatabase", ex, cancellationToken);
                await ctx.RunLock.EnsureHeldAsync(cancellationToken);
            }
        }
    }

    private async Task WaitForConnectivityAsync(RunContext ctx, string target, Exception ex, CancellationToken cancellationToken)
    {
        _logger.LogConnectivityLost(ctx.RunId, target, ctx.Remaining, ex);
        ctx.Progress.Report(ctx.Paused(SyncPauseKind.Connectivity,
            $"اتصال به {(target == "AppDatabase" ? "پایگاه‌داده" : "سرور دریافت‌کننده")} قطع شده — تلاش مجدد خودکار هر {ConnectivityWait.TotalSeconds:0} ثانیه..."));

        try { await _staging.HeartbeatAsync(ctx.RunId, cancellationToken); } catch (Exception hbEx) when (hbEx is not OperationCanceledException) { }

        var downtime = Stopwatch.StartNew();
        await Task.Delay(ConnectivityWait, cancellationToken);
        _logger.LogConnectivityRestored(ctx.RunId, target, downtime.Elapsed);
        ctx.Progress.Report(ctx.Resumed());
    }

    private async Task HoldWhilePausedAsync(RunContext ctx, PauseToken pauseToken, CancellationToken cancellationToken)
    {
        _logger.Information("Run {RunId} paused by user after {Processed}/{Total}", ctx.RunId, ctx.ProcessedTotal, ctx.Total);
        ctx.Progress.Report(ctx.Paused(SyncPauseKind.User, "متوقف‌شده توسط کاربر — برای ادامه «ادامه» را بزنید"));

        while (pauseToken.IsPaused)
        {
            var resumed = pauseToken.WaitForResumeAsync(cancellationToken);
            var tick = Task.Delay(PausedHeartbeatInterval, cancellationToken);
            var first = await Task.WhenAny(resumed, tick);
            await first;
            if (first == tick)
            {
                await ctx.RunLock.EnsureHeldAsync(cancellationToken);
                try { await _staging.HeartbeatAsync(ctx.RunId, cancellationToken); } catch (Exception ex) when (ex is not OperationCanceledException) { }
            }
        }

        _logger.Information("Run {RunId} resumed by user", ctx.RunId);
        ctx.Progress.Report(ctx.Resumed());
    }

    private sealed record RunBaseline(int Processed, int Sent, int Duplicate, int Failed);

    private sealed class RunContext(Guid runId, int total, RunBaseline baseline, IProgress<SyncProgress> progress, SyncRunLock runLock)
    {
        public Guid RunId { get; } = runId;
        public int Total { get; } = total;
        public IProgress<SyncProgress> Progress { get; } = progress;
        public SyncRunLock RunLock { get; } = runLock;

        public int Sent { get; private set; }
        public int Duplicate { get; private set; }
        public int Failed { get; private set; }
        public int ProcessedThisSession { get; private set; }
        public int ProcessedTotal => baseline.Processed + ProcessedThisSession;
        public int Remaining => Math.Max(0, Total - ProcessedTotal);

        public int CurrentPerId { get; set; }
        public string CurrentName { get; set; } = string.Empty;

        public void Count(SendStatus status)
        {
            ProcessedThisSession++;
            switch (status)
            {
                case SendStatus.Sent: Sent++; break;
                case SendStatus.Duplicate: Duplicate++; break;
                default: Failed++; break;
            }
        }

        private SyncProgress Base() => new()
        {
            Phase = SyncPhase.Sending,
            RunId = RunId,
            Total = Total,
            Processed = ProcessedTotal,
            SentCount = baseline.Sent + Sent,
            DuplicateCount = baseline.Duplicate + Duplicate,
            FailedCount = baseline.Failed + Failed,
            CurrentPerId = CurrentPerId,
            CurrentPerson = CurrentName
        };

        public SyncProgress Snapshot(SyncItemResult result)
        {
            var p = Base();
            p.LastStatus = result.Status;
            p.LastReason = result.Reason;
            p.LastChangedFields = result.ChangedFields;
            p.LastPayloadSnapshot = result.PayloadSnapshot;
            p.LastCorrelationId = result.CorrelationId;
            return p;
        }

        public SyncProgress Paused(SyncPauseKind kind, string message)
        {
            var p = Base();
            p.IsPaused = true;
            p.PauseKind = kind;
            p.PauseMessage = message;
            return p;
        }

        public SyncProgress Resumed() => Base();
    }
}
