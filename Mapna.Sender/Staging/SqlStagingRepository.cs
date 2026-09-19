using System.Data;
using Dapper;
using Mapna.Sender.Logging;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.CircuitBreaker;
using Serilog;

namespace Mapna.Sender.Staging;

public sealed class SqlStagingRepository
{
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly IAsyncPolicy _resiliencePolicy;

    private const int CircuitBreakThreshold = 5;
    private const int StageChunkSize = 5000;
    private const int StageCommandTimeoutSeconds = 30;
    public static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveHeartbeatWindow = TimeSpan.FromMinutes(2);

    public SqlStagingRepository(string connectionString, ILogger logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        _resiliencePolicy = BuildResiliencePolicy();
    }

    private IAsyncPolicy BuildResiliencePolicy()
    {
        var retry = Policy
            .Handle<SqlException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(retryCount: 3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreaker = Policy
            .Handle<SqlException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: CircuitBreakThreshold,
                durationOfBreak: CircuitBreakDuration,
                onBreak: (ex, breakDuration) =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitOpened)
                        .Warning(ex, "SQL circuit opened for {BreakSeconds:0}s after {Threshold} consecutive database failures (AppDatabase)",
                            breakDuration.TotalSeconds, CircuitBreakThreshold),
                onReset: () =>
                    _logger.ForContext("EventType", LoggingSetup.CircuitClosed)
                        .Information("SQL circuit closed - AppDatabase is reachable again"),
                onHalfOpen: () => _logger.Debug("SQL circuit half-open - probing AppDatabase with the next call"));

        return Policy.WrapAsync(retry, circuitBreaker);
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public Task EnsureSchemaAsync(CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            foreach (var batch in SchemaBatches)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        });

    public Task<StagingRun?> FindActiveRunAsync(CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT TOP 1 * FROM dbo.SyncRuns
                WHERE Status = 'Running' AND LastHeartbeatUtc >= @Since
                ORDER BY StartedAtUtc DESC;
                """;
            var row = await connection.QueryFirstOrDefaultAsync<SyncRunRow>(sql,
                new { Since = DateTime.UtcNow - ActiveHeartbeatWindow });
            return row?.ToModel();
        });

    public Task<StagingRun?> FindResumableRunAsync(CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT TOP 1 * FROM dbo.SyncRuns
                WHERE Status = 'Paused'
                   OR (Status = 'Running' AND LastHeartbeatUtc < @StaleBefore)
                ORDER BY StartedAtUtc DESC;
                """;
            var row = await connection.QueryFirstOrDefaultAsync<SyncRunRow>(sql,
                new { StaleBefore = DateTime.UtcNow - ActiveHeartbeatWindow });
            return row?.ToModel();
        });

    public Task<StagingRun> StartRunAsync(int totalCount, CancellationToken cancellationToken) =>
            _resiliencePolicy.ExecuteAsync(async () =>
        {
            var run = new StagingRun
            {
                RunId = Guid.NewGuid(),
                StartedAtUtc = DateTime.UtcNow,
                LastHeartbeatUtc = DateTime.UtcNow,
                Status = RunStatus.Running,
                TotalCount = totalCount,
                MachineName = Environment.MachineName
            };

            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                INSERT INTO dbo.SyncRuns (RunId, StartedAtUtc, LastHeartbeatUtc, Status, TotalCount, MachineName)
                VALUES (@RunId, @StartedAtUtc, @LastHeartbeatUtc, @Status, @TotalCount, @MachineName);
                """;
            await connection.ExecuteAsync(sql, new
            {
                run.RunId,
                run.StartedAtUtc,
                run.LastHeartbeatUtc,
                Status = run.Status.ToString(),
                run.TotalCount,
                run.MachineName
            });
            return run;
        });

    public async Task StageBatchAsync(Guid runId, IReadOnlyList<(int PerId, string PersonName)> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        const string sql = """
            INSERT INTO dbo.SyncItems (RunId, PerId, PersonName, Status, AttemptCount, UpdatedAtUtc)
            SELECT @RunId, PerId, PersonName, 'Pending', 0, SYSUTCDATETIME()
            FROM @Items;
            """;

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
                if (personName.Length > 200) personName = personName[..200];
                table.Rows.Add(perId, personName, nameof(StagingItemStatus.Pending), DBNull.Value, DBNull.Value, DBNull.Value);

            }

            var parameters = new DynamicParameters();
            parameters.Add("RunId", runId);
            parameters.Add("Items", table.AsTableValuedParameter("dbo.SyncItemTableType"));

            await connection.ExecuteAsync(sql, parameters, transaction, StageCommandTimeoutSeconds);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task FlushResultsAsync(Guid runId, IReadOnlyList<StagingItem> items, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            if (items.Count == 0) return;

            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var table = new DataTable();
            table.Columns.Add("PerId", typeof(int));
            table.Columns.Add("PersonName", typeof(string));
            table.Columns.Add("Status", typeof(string));
            table.Columns.Add("Reason", typeof(string));
            table.Columns.Add("ChangedFields", typeof(string));
            table.Columns.Add("PayloadSnapshot", typeof(string));

            foreach (var item in items)
            {
                table.Rows.Add(
                    item.PerId, item.PersonName, item.Status.ToString(),
                    (object?)item.Reason ?? DBNull.Value,
                    (object?)item.ChangedFields ?? DBNull.Value,
                    (object?)item.PayloadSnapshot ?? DBNull.Value);
            }

            var parameters = new DynamicParameters();
            parameters.Add("RunId", runId);
            parameters.Add("Items", table.AsTableValuedParameter("dbo.SyncItemTableType"));

            await connection.ExecuteAsync("dbo.usp_FlushSyncItemResults", parameters,
                commandType: CommandType.StoredProcedure, commandTimeout: 60);
        });

    public Task HeartbeatAsync(Guid runId, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(
                "UPDATE dbo.SyncRuns SET LastHeartbeatUtc = @Now WHERE RunId = @RunId;",
                new { Now = DateTime.UtcNow, RunId = runId });
        });

    public Task CompleteRunAsync(Guid runId, RunStatus status, string? stopReason, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                UPDATE dbo.SyncRuns
                SET Status = @Status, CompletedAtUtc = @CompletedAtUtc, StopReason = @StopReason
                WHERE RunId = @RunId;
                """;
            await connection.ExecuteAsync(sql, new
            {
                Status = status.ToString(),
                CompletedAtUtc = DateTime.UtcNow,
                StopReason = stopReason,
                RunId = runId
            });
        });

    public Task<HashSet<int>> GetPendingPerIdsAsync(Guid runId, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            var ids = await connection.QueryAsync<int>(
                "SELECT PerId FROM dbo.SyncItems WHERE RunId = @RunId AND Status = 'Pending';",
                new { RunId = runId });
            return ids.ToHashSet();
        });

    public Task<List<StagingItem>> GetItemsAsync(Guid runId, StagingItemStatus? statusFilter, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            var sql = statusFilter is null
                ? "SELECT * FROM dbo.SyncItems WHERE RunId = @RunId ORDER BY UpdatedAtUtc;"
                : "SELECT * FROM dbo.SyncItems WHERE RunId = @RunId AND Status = @Status ORDER BY UpdatedAtUtc;";
            var rows = await connection.QueryAsync<SyncItemRow>(sql,
                new { RunId = runId, Status = statusFilter?.ToString() });
            return rows.Select(r => r.ToModel()).ToList();
        });

    public Task<StagingRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken) =>
        _resiliencePolicy.ExecuteAsync(async () =>
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            var row = await connection.QueryFirstOrDefaultAsync<SyncRunRow>(
                "SELECT * FROM dbo.SyncRuns WHERE RunId = @RunId;", new { RunId = runId });
            return row?.ToModel();
        });

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
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            LastHeartbeatUtc = LastHeartbeatUtc,
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
        CREATE OR ALTER PROCEDURE dbo.usp_FlushSyncItemResults
            @RunId UNIQUEIDENTIFIER,
            @Items dbo.SyncItemTableType READONLY
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

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

            UPDATE dbo.SyncRuns
            SET
                LastHeartbeatUtc = SYSUTCDATETIME(),
                ProcessedCount = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status <> 'Pending'),
                SentCount      = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status = 'Sent'),
                DuplicateCount = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status = 'Duplicate'),
                FailedCount    = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status IN ('ValidationFailed', 'SendFailed'))
            WHERE RunId = @RunId;

            COMMIT TRANSACTION;
        END
        """
    ];
}