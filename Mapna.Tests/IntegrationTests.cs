using Dapper;
using Mapna.Contracts;
using Mapna.LogData;
using Mapna.Sender;
using Mapna.Sender.Staging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Mapna.Tests;

/// <summary>Runs only when MAPNA_TEST_SQL points at a SQL Server we may create throw-away databases on,
/// e.g. "Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True".</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAPNA_TEST_SQL")))
            Skip = "Set MAPNA_TEST_SQL to run SQL Server integration tests.";
    }
}

/// <summary>One fresh database per test: EF migrations applied, staging schema created, empty source table.</summary>
internal sealed class TestEnvironment : IAsyncDisposable
{
    private const string ApiKey = "integration-test-key-7f3a";
    private readonly string _master;
    private readonly string _dbName;
    private readonly ReceiverFactory _receiver;

    public string ConnectionString { get; }
    public AppSettings Settings { get; }
    public SqlStagingRepository Staging { get; }
    public ILogger Logger { get; } = new LoggerConfiguration().CreateLogger();

    private TestEnvironment(string master, string dbName, string cs)
    {
        _master = master;
        _dbName = dbName;
        ConnectionString = cs;
        Settings = new AppSettings
        {
            SourceConnectionString = cs,
            AppConnectionString = cs,
            ReceiverApiBaseUrl = "http://localhost/",
            ReceiverApiKey = ApiKey,
            LogsDirectory = Path.GetTempPath()
        };
        Staging = new SqlStagingRepository(cs, Logger);
        _receiver = new ReceiverFactory(cs, ApiKey);
    }

    public static async Task<TestEnvironment> CreateAsync()
    {
        var master = Environment.GetEnvironmentVariable("MAPNA_TEST_SQL")!;
        var dbName = "MapnaEtlTests_" + Guid.NewGuid().ToString("N")[..10];
        await using (var c = new SqlConnection(master))
            await c.ExecuteAsync($"CREATE DATABASE [{dbName}]");

        var cs = new SqlConnectionStringBuilder(master) { InitialCatalog = dbName }.ConnectionString;
        var env = new TestEnvironment(master, dbName, cs);
        try
        {
            await env.InitializeSchemaAsync();
        }
        catch
        {
            await env.DisposeAsync();   // never leak a half-built database
            throw;
        }
        return env;
    }

    private async Task InitializeSchemaAsync()
    {
        var cs = ConnectionString;
        await using (var db = LogsDbContextFactory.Create(cs))
            await db.Database.MigrateAsync();

        await Staging.EnsureSchemaAsync(CancellationToken.None);
        await using (var c = new SqlConnection(cs))
        {
            // No primary key on purpose, so a duplicate PER_ID (a real-world data defect) can be reproduced.
            await c.ExecuteAsync("""
                CREATE TABLE dbo.PERSONEL_Sender (
                    PER_ID INT NOT NULL, PER_NAME NVARCHAR(200), PER_SURNAME NVARCHAR(200), PER_STATUS INT NOT NULL,
                    SEX_CODE NVARCHAR(10), PER_EMAIL NVARCHAR(256), MOBIL_NO NVARCHAR(30), PHONE NVARCHAR(30),
                    PER_ADDR NVARCHAR(500), PER_LNAME NVARCHAR(200), PER_LSURNAME NVARCHAR(200), BORN_DATE NVARCHAR(20),
                    NATIONAL_CODE NVARCHAR(30), USER_PRINCIPAL_NAME NVARCHAR(256), PER_CONTRACT NVARCHAR(50), COMPANY_ID NVARCHAR(50));
                """);
        }
    }

    public async Task SeedAsync(IEnumerable<PersonnelRecord> records)
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.ExecuteAsync("""
            INSERT INTO dbo.PERSONEL_Sender (PER_ID, PER_NAME, PER_SURNAME, PER_STATUS, SEX_CODE, PER_EMAIL, MOBIL_NO, PHONE, PER_ADDR,
                PER_LNAME, PER_LSURNAME, BORN_DATE, NATIONAL_CODE, USER_PRINCIPAL_NAME, PER_CONTRACT, COMPANY_ID)
            VALUES (@PerId, @PerName, @PerSurname, @PerStatus, @SexCode, @PerEmail, @MobileNo, @Phone, @PerAddr,
                @PerLName, @PerLSurname, @BornDate, @NationalCode, @UserPrincipalName, @PerContract, @CompanyId);
            """, records);
    }

    public async Task<int> ExecuteAsync(string sql)
    {
        await using var c = new SqlConnection(ConnectionString);
        return await c.ExecuteAsync(sql);
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var c = new SqlConnection(ConnectionString);
        return await c.ExecuteScalarAsync<T>(sql) ?? throw new InvalidOperationException(sql);
    }

    public SyncOrchestrator Orchestrator(Func<HttpMessageHandler, HttpMessageHandler>? wrap = null) =>
        new(Settings, Staging, Logger)
        {
            PrimaryHttpHandler = () => wrap is null ? _receiver.Server.CreateHandler() : wrap(_receiver.Server.CreateHandler())
        };

    public async Task<StagingRun> RunAsync(Guid? resume = null, CancellationToken ct = default, PauseToken pause = default,
        Func<HttpMessageHandler, HttpMessageHandler>? wrap = null, IProgress<SyncProgress>? progress = null)
    {
        await Orchestrator(wrap).RunAsync(progress ?? new Progress<SyncProgress>(), ct, resume, pause);
        return (await Staging.GetRecentRunsAsync(1, CancellationToken.None)).Single();
    }

    public async ValueTask DisposeAsync()
    {
        await _receiver.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var c = new SqlConnection(_master);
        await c.ExecuteAsync($"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}];");
    }

    private sealed class ReceiverFactory(string cs, string apiKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:AppDatabase", cs);
            builder.UseSetting("Security:ApiKeys:SenderService", apiKey);
        }
    }
}

public class IntegrationTests
{
    private static IEnumerable<PersonnelRecord> People(int count, int start = 1) =>
        Enumerable.Range(start, count).Select(i => TestData.Record(i));

    [SqlFact]
    public async Task Changes_are_sent_exactly_once_and_decision_state_survives_across_runs()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await env.SeedAsync(People(120));

        var run1 = await env.RunAsync();
        Assert.Equal((RunStatus.Completed, 120, 0, 0), (run1.Status, run1.SentCount, run1.DuplicateCount, run1.FailedCount));
        Assert.Equal(120, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Personnel"));
        Assert.Equal(120, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SendStates WHERE PayloadSnapshot IS NOT NULL"));

        // Every audited send can be traced to the receiver's own log through the correlation id.
        Assert.Equal(120, await env.ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.SendLogs s JOIN dbo.ReceiveLogs r ON r.CorrelationId = s.CorrelationId
            WHERE s.Status = 'Sent' AND r.Status = 'Inserted'
            """));

        var run2 = await env.RunAsync();
        Assert.Equal((0, 120), (run2.SentCount, run2.DuplicateCount));

        await env.ExecuteAsync("UPDATE dbo.PERSONEL_Sender SET PER_SURNAME = N'کریمی' WHERE PER_ID <= 80");
        var run3 = await env.RunAsync();
        Assert.Equal((80, 40), (run3.SentCount, run3.DuplicateCount));
        Assert.Equal(80, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceiveLogs WHERE Status = 'Updated'"));

        // Regression test for the ChangeTracker.Clear() bug. The old code lost every state update after the first
        // flush, so this run would re-send most of the 80 records again.
        var run4 = await env.RunAsync();
        Assert.Equal((0, 120), (run4.SentCount, run4.DuplicateCount));

        // A SendLogs cleanup must not affect decisions any more.
        await env.ExecuteAsync("DELETE FROM dbo.SendLogs");
        var run5 = await env.RunAsync();
        Assert.Equal((0, 120), (run5.SentCount, run5.DuplicateCount));
    }

    [SqlFact]
    public async Task National_code_collision_is_rejected_instead_of_creating_a_second_person()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var shared = TestData.NationalCode(999);
        await env.SeedAsync([TestData.Record(1, r => r.NationalCode = shared), TestData.Record(2, r => r.NationalCode = shared)]);

        var run = await env.RunAsync();

        Assert.Equal((1, 1), (run.SentCount, run.FailedCount));
        Assert.Equal(1, await env.ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Personnel WHERE NationalCode = '{shared}'"));
        Assert.Equal(1, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceiveLogs WHERE Status = 'RejectedNationalCodeConflict'"));
        Assert.Contains("409", await env.ScalarAsync<string>("SELECT Reason FROM dbo.SyncItems WHERE Status = 'SendFailed'"));
    }

    [SqlFact]
    public async Task Duplicate_perId_in_source_is_quarantined_and_does_not_block_the_rest()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await env.SeedAsync([.. People(10), TestData.Record(5, r => { r.PerName = "نفر دوم"; r.NationalCode = TestData.NationalCode(5005); })]);

        var run = await env.RunAsync();

        Assert.Equal((RunStatus.CompletedWithFailures, 9, 1), (run.Status, run.SentCount, run.FailedCount));
        Assert.Equal(0, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Personnel WHERE PerId = 5"));
        Assert.Equal("ValidationFailed", await env.ScalarAsync<string>("SELECT Status FROM dbo.SyncItems WHERE PerId = 5"));
    }

    [SqlFact]
    public async Task Empty_latin_name_and_persian_digits_are_handled_end_to_end()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await env.SeedAsync([TestData.Record(1, r => { r.PerLName = ""; r.PerLSurname = ""; r.MobileNo = "۰۹۱۲۳۴۵۶۷۸۹"; })]);

        var run = await env.RunAsync();

        Assert.Equal(1, run.SentCount);
        Assert.Equal("09123456789", await env.ScalarAsync<string>("SELECT MobileNo FROM dbo.Personnel WHERE PerId = 1"));
    }

    [SqlFact]
    public async Task A_second_instance_cannot_run_while_the_lock_is_held()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await env.SeedAsync(People(5));

        await using (var otherInstance = await SyncRunLock.TryAcquireAsync(env.ConnectionString, CancellationToken.None))
        {
            Assert.NotNull(otherInstance);
            await Assert.ThrowsAsync<ConcurrentRunDetectedException>(() => env.RunAsync());
            Assert.Null(await SyncRunLock.TryAcquireAsync(env.ConnectionString, CancellationToken.None));
        }

        var run = await env.RunAsync();
        Assert.Equal(5, run.SentCount);
    }

    [SqlFact]
    public async Task Stopped_run_is_resumable_and_every_record_is_applied_exactly_once()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await env.SeedAsync(People(60));

        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            env.RunAsync(ct: cts.Token, wrap: inner => new CancelAfterResponses(inner, 25, cts)));

        var stopped = await env.Staging.FindResumableRunAsync(CancellationToken.None);
        Assert.NotNull(stopped);
        Assert.Equal(RunStatus.Paused, stopped!.Status);
        // 25 if the stop landed between records; 24 if it hit while the 25th response was still being read.
        // In that case the receiver HAS applied #25, the sender doesn't know it, and resume re-sends it.
        Assert.InRange(stopped.SentCount, 24, 25);

        var resumed = await env.RunAsync(resume: stopped.RunId);
        Assert.Equal(stopped.RunId, resumed.RunId);
        Assert.Equal((RunStatus.Completed, 60, 60), (resumed.Status, resumed.ProcessedCount, resumed.SentCount));

        // At-least-once delivery + idempotent upsert = each person written exactly once at the destination.
        Assert.Equal(60, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Personnel"));
        Assert.Equal(60, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceiveLogs WHERE Status = 'Inserted'"));
        Assert.Equal(0, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceiveLogs WHERE Status = 'Updated'"));
        Assert.InRange(await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceiveLogs WHERE Status = 'Duplicate'"), 0, 1);
    }

    [SqlFact]
    public async Task Pause_holds_between_records_and_resume_completes()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await env.SeedAsync(People(40));

        var pause = new PauseTokenSource();
        var paused = new TaskCompletionSource();
        var progress = new InlineProgress(p =>
        {
            if (p.IsRecordResult && p.Processed == 10) pause.Pause();
            if (p is { IsPaused: true, PauseKind: SyncPauseKind.User }) paused.TrySetResult();
        });

        var runTask = env.RunAsync(pause: pause.Token, progress: progress);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var sentWhilePaused = await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Personnel");
        await Task.Delay(1500);
        Assert.Equal(sentWhilePaused, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Personnel"));
        Assert.Equal(10, sentWhilePaused);

        pause.Resume();
        var run = await runTask;
        Assert.Equal((RunStatus.Completed, 40), (run.Status, run.SentCount));
    }

    [SqlFact]
    public async Task Replaying_a_flush_after_an_ambiguous_commit_does_not_duplicate_audit_rows()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var run = await env.Staging.StartRunAsync(2, CancellationToken.None);
        await env.Staging.StageBatchAsync(run.RunId, [(1, "a"), (2, "b")], CancellationToken.None);
        SyncItemResult[] batch =
        [
            new() { PerId = 1, PersonName = "a", Status = SendStatus.Sent, PayloadSnapshot = "{}", ConfirmedByReceiver = true },
            new() { PerId = 2, PersonName = "b", Status = SendStatus.SendFailed, Reason = new string('x', 5000) }   // over-long: truncated, not an infinite retry loop
        ];

        await env.Staging.FlushResultsAsync(run.RunId, batch, CancellationToken.None);
        await env.Staging.FlushResultsAsync(run.RunId, batch, CancellationToken.None);

        Assert.Equal(2, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SendLogs"));
        Assert.Equal(1, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SendStates WHERE PayloadSnapshot IS NOT NULL"));
        Assert.Equal(1, await env.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SendStates WHERE PerId = 2 AND PayloadSnapshot IS NULL"));
    }

    private sealed class InlineProgress(Action<SyncProgress> onReport) : IProgress<SyncProgress>
    {
        public void Report(SyncProgress value) => onReport(value);
    }

    /// <summary>Simulates the user pressing Stop right after the Nth response came back.</summary>
    private sealed class CancelAfterResponses(HttpMessageHandler inner, int count, CancellationTokenSource cts) : DelegatingHandler(inner)
    {
        private int _responses;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, CancellationToken.None);
            if (Interlocked.Increment(ref _responses) == count)
                await cts.CancelAsync();
            return response;
        }
    }
}
