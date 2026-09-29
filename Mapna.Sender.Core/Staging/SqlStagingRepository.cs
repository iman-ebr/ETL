using System.Data;
using Dapper;
using Mapna.LogData;
using Mapna.Sender.Logging;
using Microsoft.Data.SqlClient;
using Polly;
using Serilog;

namespace Mapna.Sender.Staging;

public sealed class SqlStagingRepository
{
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly IAsyncPolicy _resiliencePolicy;

    private const int CircuitBreakThreshold = 5;
    private const int StageChunkSize = 5000;
    private const int StageCommandTimeoutSeconds = 60;
    public static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);

    public SqlStagingRepository(string connectionString, ILogger logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        _resiliencePolicy = BuildResiliencePolicy();
    }

    private IAsyncPolicy BuildResiliencePolicy()
    {
        // Transient errors only. The old policy retried every SqlException, including "string or binary data
        // would be truncated", so one over-long value opened the breaker and the orchestrator waited and retried
        // the same batch forever.
        var retry = Policy
            .Handle<Exception>(SqlTransientErrors.IsTransient)
            .WaitAndRetryAsync(retryCount: 3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreaker = Policy
            .Handle<Exception>(SqlTransientErrors.IsTransient)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: CircuitBreakThreshold,
                durationOfBreak: CircuitBreakDuration,
                onBreak: (ex, breakDuration) =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitOpened)
                        .Warning(ex, "SQL circuit opened for {BreakSeconds:0}s after {Threshold} consecutive transient failures (AppDatabase)",
                            breakDuration.TotalSeconds, CircuitBreakThreshold),
                onReset: () =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitClosed)
                        .Information("SQL circuit closed - AppDatabase is reachable again"),
                onHalfOpen: () => _logger.Debug("SQL circuit half-open - probing AppDatabase with the next call"));

        return Policy.WrapAsync(retry, circuitBreaker);
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public Task EnsureSchemaAsync(CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            foreach (var batch in SchemaBatches)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }, cancellationToken);

    /// <summary>A run that is 'Running' AND whose run lock is currently held, i.e. really alive right now. No clocks involved.</summary>
    public Task<StagingRun?> FindActiveRunAsync(CancellationToken cancellationToken) =>
        QuerySingleRunAsync("""
            SELECT TOP 1 * FROM dbo.SyncRuns
            WHERE Status = 'Running'
              AND APPLOCK_TEST('public', @Resource, 'Exclusive', 'Session') = 0
            ORDER BY StartedAtUtc DESC;
            """, cancellationToken);

    /// <summary>
    /// Paused runs, or 'Running' runs whose process is gone (nobody holds the run lock). The old version compared
    /// a client-clock heartbeat with the reader's client clock, so a skewed machine could "resume" a live run.
    /// </summary>
    public Task<StagingRun?> FindResumableRunAsync(CancellationToken cancellationToken) =>
        QuerySingleRunAsync("""
            SELECT TOP 1 * FROM dbo.SyncRuns
            WHERE Status = 'Paused'
               OR (Status = 'Running' AND APPLOCK_TEST('public', @Resource, 'Exclusive', 'Session') = 1)
            ORDER BY StartedAtUtc DESC;
            """, cancellationToken);

    private Task<StagingRun?> QuerySingleRunAsync(string sql, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            var row = await connection.QueryFirstOrDefaultAsync<SyncRunRow>(
                new CommandDefinition(sql, new { Resource = SyncRunLock.Resource }, cancellationToken: ct));
            return row?.ToModel();
        }, cancellationToken);

    public Task<StagingRun> StartRunAsync(int totalCount, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            var runId = Guid.NewGuid();
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            const string sql = """
                INSERT INTO dbo.SyncRuns (RunId, StartedAtUtc, LastHeartbeatUtc, Status, TotalCount, MachineName)
                OUTPUT inserted.*
                VALUES (@RunId, SYSUTCDATETIME(), SYSUTCDATETIME(), 'Running', @TotalCount, @MachineName);
                """;
            var row = await connection.QuerySingleAsync<SyncRunRow>(new CommandDefinition(sql,
                new { RunId = runId, TotalCount = totalCount, MachineName = Environment.MachineName }, cancellationToken: ct));
            return row.ToModel();
        }, cancellationToken);

    public Task MarkRunResumedAsync(Guid runId, int totalCount, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.SyncRuns
                SET Status = 'Running', CompletedAtUtc = NULL, StopReason = NULL, LastHeartbeatUtc = SYSUTCDATETIME(),
                    MachineName = @MachineName,
                    TotalCount = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId)
                WHERE RunId = @RunId;
                """, new { RunId = runId, MachineName = Environment.MachineName }, cancellationToken: ct));
        }, cancellationToken);

    /// <summary>Stages PerIds as Pending. Idempotent: PerIds already staged for the run are left alone, so the same
    /// call also adds records that appeared in the source after the run started (on resume).</summary>
    public async Task<int> StageBatchAsync(Guid runId, IReadOnlyList<(int PerId, string PersonName)> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return 0;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        const string sql = """
            INSERT INTO dbo.SyncItems (RunId, PerId, PersonName, Status, AttemptCount, UpdatedAtUtc)
            SELECT @RunId, i.PerId, i.PersonName, 'Pending', 0, SYSUTCDATETIME()
            FROM @Items i
            WHERE NOT EXISTS (SELECT 1 FROM dbo.SyncItems s WHERE s.RunId = @RunId AND s.PerId = i.PerId);
            """;

        var inserted = 0;
        for (var offset = 0; offset < items.Count; offset += StageChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = Math.Min(offset + StageChunkSize, items.Count);

            var table = new DataTable();
            table.Columns.Add("PerId", typeof(int));
            table.Columns.Add("PersonName", typeof(string));
            table.Columns.Add("Status", typeof(string));
            table.Columns.Add("Reason", typeof(string));
            table.Columns.Add("ChangedFields", typeof(string));
            table.Columns.Add("PayloadSnapshot", typeof(string));

            for (var i = offset; i < end; i++)
            {
                var (perId, personName) = items[i];
                table.Rows.Add(perId, Fit(personName, 200), nameof(StagingItemStatus.Pending), DBNull.Value, DBNull.Value, DBNull.Value);
            }

            var parameters = new DynamicParameters();
            parameters.Add("RunId", runId);
            parameters.Add("Items", table.AsTableValuedParameter("dbo.SyncItemTableType"));

            inserted += await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, StageCommandTimeoutSeconds, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    /// <summary>
    /// ONE transaction for: the audit rows (SendLogs), the decision state (SendStates) and the run's item status
    /// (SyncItems + counters). Before, these were an EF SaveChanges plus a separate Dapper call, so a crash between
    /// them left the three stores disagreeing. The finally-block even marked staging items done after a FAILED
    /// SendLogs save, which lost audit rows for records that really were sent.
    /// Replaying the same batch (after an ambiguous commit) is idempotent: CorrelationId is unique in SendLogs,
    /// and the MERGEs converge.
    /// </summary>
    public Task FlushResultsAsync(Guid runId, IReadOnlyList<SyncItemResult> items, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            if (items.Count == 0) return;

            var table = new DataTable();
            table.Columns.Add("PerId", typeof(int));
            table.Columns.Add("PersonName", typeof(string));
            table.Columns.Add("Status", typeof(string));
            table.Columns.Add("Reason", typeof(string));
            table.Columns.Add("ChangedFields", typeof(string));
            table.Columns.Add("PayloadSnapshot", typeof(string));
            table.Columns.Add("ConfirmedByReceiver", typeof(bool));
            table.Columns.Add("CorrelationId", typeof(Guid));
            table.Columns.Add("OccurredAtUtc", typeof(DateTime));

            foreach (var item in items)
            {
                table.Rows.Add(
                    item.PerId,
                    Fit(item.PersonName, 200),
                    item.Status.ToString(),
                    (object?)Fit(item.Reason, LogFieldLimit.ReasonMaxLength) ?? DBNull.Value,
                    (object?)Fit(item.ChangedFields, LogFieldLimit.ChangedFieldsMaxLength) ?? DBNull.Value,
                    (object?)item.PayloadSnapshot ?? DBNull.Value,
                    item.ConfirmedByReceiver,
                    item.CorrelationId,
                    item.OccurredAtUtc);
            }

            var parameters = new DynamicParameters();
            parameters.Add("RunId", runId);
            parameters.Add("Items", table.AsTableValuedParameter("dbo.SyncResultTableType"));

            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition("dbo.usp_FlushSyncResults", parameters,
                commandType: CommandType.StoredProcedure, commandTimeout: 60, cancellationToken: ct));
        }, cancellationToken);

    public Task HeartbeatAsync(Guid runId, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.SyncRuns SET LastHeartbeatUtc = SYSUTCDATETIME() WHERE RunId = @RunId;",
                new { RunId = runId }, cancellationToken: ct));
        }, cancellationToken);

    /// <summary>For <see cref="RunStatus.Completed"/>, the database decides between Completed and CompletedWithFailures from
    /// the real item counts. The old code used this session's in-memory counter, which is wrong for resumed runs.</summary>
    public Task CompleteRunAsync(Guid runId, RunStatus status, string? stopReason, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            const string sql = """
                UPDATE r
                SET Status = CASE WHEN @Status = 'Completed' AND c.Failed > 0 THEN 'CompletedWithFailures' ELSE @Status END,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    StopReason = @StopReason,
                    ProcessedCount = c.Processed, SentCount = c.Sent, DuplicateCount = c.Duplicate, FailedCount = c.Failed
                FROM dbo.SyncRuns r
                CROSS APPLY (
                    SELECT SUM(CASE WHEN Status <> 'Pending' THEN 1 ELSE 0 END) AS Processed,
                           SUM(CASE WHEN Status = 'Sent' THEN 1 ELSE 0 END) AS Sent,
                           SUM(CASE WHEN Status = 'Duplicate' THEN 1 ELSE 0 END) AS Duplicate,
                           SUM(CASE WHEN Status IN ('ValidationFailed', 'SendFailed') THEN 1 ELSE 0 END) AS Failed
                    FROM dbo.SyncItems WHERE RunId = r.RunId) c
                WHERE r.RunId = @RunId;
                """;
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Status = status.ToString(),
                StopReason = Fit(stopReason, 500),
                RunId = runId
            }, cancellationToken: ct));
        }, cancellationToken);

    public Task<HashSet<int>> GetPendingPerIdsAsync(Guid runId, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            var ids = await connection.QueryAsync<int>(new CommandDefinition(
                "SELECT PerId FROM dbo.SyncItems WHERE RunId = @RunId AND Status = 'Pending';",
                new { RunId = runId }, cancellationToken: ct));
            return ids.ToHashSet();
        }, cancellationToken);

    public Task<List<StagingItem>> GetItemsAsync(Guid runId, StagingItemStatus? statusFilter, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            var sql = statusFilter is null
                ? "SELECT * FROM dbo.SyncItems WHERE RunId = @RunId ORDER BY UpdatedAtUtc;"
                : "SELECT * FROM dbo.SyncItems WHERE RunId = @RunId AND Status = @Status ORDER BY UpdatedAtUtc;";
            var rows = await connection.QueryAsync<SyncItemRow>(new CommandDefinition(sql,
                new { RunId = runId, Status = statusFilter?.ToString() }, cancellationToken: ct));
            return rows.Select(r => r.ToModel()).ToList();
        }, cancellationToken);

    public Task<StagingRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            var row = await connection.QueryFirstOrDefaultAsync<SyncRunRow>(new CommandDefinition(
                "SELECT * FROM dbo.SyncRuns WHERE RunId = @RunId;", new { RunId = runId }, cancellationToken: ct));
            return row?.ToModel();
        }, cancellationToken);

    public Task<List<StagingRun>> GetRecentRunsAsync(int top, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async ct =>
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            var rows = await connection.QueryAsync<SyncRunRow>(new CommandDefinition(
                "SELECT TOP (@Top) * FROM dbo.SyncRuns ORDER BY StartedAtUtc DESC;", new { Top = top }, cancellationToken: ct));
            return rows.Select(r => r.ToModel()).ToList();
        }, cancellationToken);

    private static string? Fit(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..(max - 1)] + "…";

    private sealed class SyncRunRow
    {
        public Guid RunId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime LastHeartbeatUtc { get; set; }
        public string Status { get; set; } = "";
        public int TotalCount { get; set; }
        public int ProcessedCount { get; set; }
        public int SentCount { get; set; }
        public int DuplicateCount { get; set; }
        public int FailedCount { get; set; }
        public string? MachineName { get; set; }
        public string? StopReason { get; set; }

        public StagingRun ToModel() => new()
        {
            RunId = RunId,
            StartedAtUtc = DateTime.SpecifyKind(StartedAtUtc, DateTimeKind.Utc),
            CompletedAtUtc = CompletedAtUtc is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null,
            LastHeartbeatUtc = DateTime.SpecifyKind(LastHeartbeatUtc, DateTimeKind.Utc),
            Status = Enum.Parse<RunStatus>(Status),
            TotalCount = TotalCount,
            ProcessedCount = ProcessedCount,
            SentCount = SentCount,
            DuplicateCount = DuplicateCount,
            FailedCount = FailedCount,
            MachineName = MachineName ?? "",
            StopReason = StopReason
        };
    }

    private sealed class SyncItemRow
    {
        public Guid RunId { get; set; }
        public int PerId { get; set; }
        public string? PersonName { get; set; }
        public string Status { get; set; } = "";
        public string? Reason { get; set; }
        public string? ChangedFields { get; set; }
        public string? PayloadSnapshot { get; set; }
        public int AttemptCount { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public StagingItem ToModel() => new()
        {
            RunId = RunId,
            PerId = PerId,
            PersonName = PersonName ?? "",
            Status = Enum.Parse<StagingItemStatus>(Status),
            Reason = Reason,
            ChangedFields = ChangedFields,
            PayloadSnapshot = PayloadSnapshot,
            AttemptCount = AttemptCount,
            UpdatedAtUtc = DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)
        };
    }

    // NOTE: user-defined table types cannot be ALTERed. A changed TVP needs a NEW name (as SyncResultTableType is).
    // "IF TYPE_ID(...) IS NULL CREATE" silently keeps an old shape otherwise.
    private static readonly string[] SchemaBatches =
    [
        """
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncRuns' AND schema_id = SCHEMA_ID('dbo'))
        BEGIN
            CREATE TABLE dbo.SyncRuns
            (
                RunId               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SyncRuns PRIMARY KEY,
                StartedAtUtc        DATETIME2(3)      NOT NULL,
                CompletedAtUtc      DATETIME2(3)      NULL,
                LastHeartbeatUtc    DATETIME2(3)      NOT NULL,
                Status              NVARCHAR(30)      NOT NULL,
                TotalCount          INT               NOT NULL,
                ProcessedCount      INT               NOT NULL CONSTRAINT DF_SyncRuns_Processed DEFAULT (0),
                SentCount           INT               NOT NULL CONSTRAINT DF_SyncRuns_Sent DEFAULT (0),
                DuplicateCount      INT               NOT NULL CONSTRAINT DF_SyncRuns_Duplicate DEFAULT (0),
                FailedCount         INT               NOT NULL CONSTRAINT DF_SyncRuns_Failed DEFAULT (0),
                MachineName         NVARCHAR(100)     NULL,
                StopReason          NVARCHAR(500)     NULL
            );
            CREATE INDEX IX_SyncRuns_Status ON dbo.SyncRuns (Status);
            CREATE INDEX IX_SyncRuns_StartedAtUtc ON dbo.SyncRuns (StartedAtUtc DESC);
        END
        """,
        """
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncItems' AND schema_id = SCHEMA_ID('dbo'))
        BEGIN
            CREATE TABLE dbo.SyncItems
            (
                RunId               UNIQUEIDENTIFIER NOT NULL,
                PerId               INT              NOT NULL,
                PersonName          NVARCHAR(200)    NULL,
                Status              NVARCHAR(30)     NOT NULL,
                Reason              NVARCHAR(1000)   NULL,
                ChangedFields       NVARCHAR(1000)   NULL,
                PayloadSnapshot     NVARCHAR(MAX)    NULL,
                AttemptCount        INT              NOT NULL CONSTRAINT DF_SyncItems_Attempt DEFAULT (0),
                UpdatedAtUtc        DATETIME2(3)     NOT NULL,
                CONSTRAINT PK_SyncItems PRIMARY KEY (RunId, PerId),
                CONSTRAINT FK_SyncItems_SyncRuns FOREIGN KEY (RunId) REFERENCES dbo.SyncRuns (RunId)
            );
            CREATE INDEX IX_SyncItems_RunId_Status ON dbo.SyncItems (RunId, Status);
        END
        """,
        """
        IF TYPE_ID(N'dbo.SyncItemTableType') IS NULL
        BEGIN
            CREATE TYPE dbo.SyncItemTableType AS TABLE
            (
                PerId               INT              NOT NULL,
                PersonName          NVARCHAR(200)    NULL,
                Status              NVARCHAR(30)     NOT NULL,
                Reason              NVARCHAR(1000)   NULL,
                ChangedFields       NVARCHAR(1000)   NULL,
                PayloadSnapshot     NVARCHAR(MAX)    NULL,
                PRIMARY KEY (PerId)
            );
        END
        """,
        """
        IF TYPE_ID(N'dbo.SyncResultTableType') IS NULL
        BEGIN
            CREATE TYPE dbo.SyncResultTableType AS TABLE
            (
                PerId               INT              NOT NULL PRIMARY KEY,
                PersonName          NVARCHAR(200)    NULL,
                Status              NVARCHAR(30)     NOT NULL,
                Reason              NVARCHAR(500)    NULL,
                ChangedFields       NVARCHAR(500)    NULL,
                PayloadSnapshot     NVARCHAR(MAX)    NULL,
                ConfirmedByReceiver BIT              NOT NULL,
                CorrelationId       UNIQUEIDENTIFIER NOT NULL,
                OccurredAtUtc       DATETIME2(7)     NOT NULL
            );
        END
        """,
        """
        CREATE OR ALTER PROCEDURE dbo.usp_FlushSyncResults
            @RunId UNIQUEIDENTIFIER,
            @Items dbo.SyncResultTableType READONLY
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            -- 1) Audit trail. Idempotent on CorrelationId (unique filtered index IX_SendLogs_CorrelationId).
            INSERT INTO dbo.SendLogs (PerId, OccurredAtUtc, Status, Reason, ChangedFields, PayloadSnapshot, RunId, CorrelationId)
            SELECT i.PerId, i.OccurredAtUtc, i.Status, i.Reason, i.ChangedFields, i.PayloadSnapshot, @RunId, i.CorrelationId
            FROM @Items i
            WHERE NOT EXISTS (SELECT 1 FROM dbo.SendLogs l WHERE l.CorrelationId = i.CorrelationId);

            -- 2) Decision state. Only a receiver-confirmed send may move the snapshot forward.
            --    Duplicate / ValidationFailed leave the state exactly as it was (same as before the refactor).
            MERGE dbo.SendStates WITH (HOLDLOCK) AS t
            USING (SELECT * FROM @Items WHERE Status IN (N'Sent', N'SendFailed')) AS s
                ON t.PerId = s.PerId
            WHEN MATCHED THEN UPDATE SET
                t.LastStatus       = s.Status,
                t.LastAttemptAtUtc = SYSUTCDATETIME(),
                t.PayloadSnapshot  = CASE WHEN s.ConfirmedByReceiver = 1 THEN s.PayloadSnapshot ELSE t.PayloadSnapshot END,
                t.LastSentAtUtc    = CASE WHEN s.ConfirmedByReceiver = 1 THEN SYSUTCDATETIME() ELSE t.LastSentAtUtc END
            WHEN NOT MATCHED THEN
                INSERT (PerId, PayloadSnapshot, LastSentAtUtc, LastStatus, LastAttemptAtUtc)
                VALUES (s.PerId,
                        CASE WHEN s.ConfirmedByReceiver = 1 THEN s.PayloadSnapshot END,
                        CASE WHEN s.ConfirmedByReceiver = 1 THEN SYSUTCDATETIME() END,
                        s.Status, SYSUTCDATETIME());

            -- 3) Run item status + counters.
            MERGE dbo.SyncItems AS target
            USING @Items AS source
                ON target.RunId = @RunId AND target.PerId = source.PerId
            WHEN MATCHED THEN
                UPDATE SET
                    target.PersonName      = source.PersonName,
                    target.Status          = source.Status,
                    target.Reason          = source.Reason,
                    target.ChangedFields   = source.ChangedFields,
                    target.PayloadSnapshot = source.PayloadSnapshot,
                    target.AttemptCount    = target.AttemptCount + 1,
                    target.UpdatedAtUtc    = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (RunId, PerId, PersonName, Status, Reason, ChangedFields, PayloadSnapshot, AttemptCount, UpdatedAtUtc)
                VALUES (@RunId, source.PerId, source.PersonName, source.Status, source.Reason, source.ChangedFields, source.PayloadSnapshot, 1, SYSUTCDATETIME());

            UPDATE r
            SET LastHeartbeatUtc = SYSUTCDATETIME(),
                ProcessedCount = c.Processed, SentCount = c.Sent, DuplicateCount = c.Duplicate, FailedCount = c.Failed
            FROM dbo.SyncRuns r
            CROSS APPLY (
                SELECT SUM(CASE WHEN Status <> 'Pending' THEN 1 ELSE 0 END) AS Processed,
                       SUM(CASE WHEN Status = 'Sent' THEN 1 ELSE 0 END) AS Sent,
                       SUM(CASE WHEN Status = 'Duplicate' THEN 1 ELSE 0 END) AS Duplicate,
                       SUM(CASE WHEN Status IN ('ValidationFailed', 'SendFailed') THEN 1 ELSE 0 END) AS Failed
                FROM dbo.SyncItems WHERE RunId = @RunId) c
            WHERE r.RunId = @RunId;

            COMMIT TRANSACTION;
        END
        """
    ];
}
