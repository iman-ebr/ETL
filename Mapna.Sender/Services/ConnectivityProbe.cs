using System.Net.Http;
using Mapna.LogData;
using Mapna.Sender.Staging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Mapna.Sender.Services;

public sealed record EnvironmentStatus(bool AppDatabaseReachable, IReadOnlyList<string> PendingMigrations, string? Error)
{
    public bool IsReady => AppDatabaseReachable && PendingMigrations.Count == 0 && Error is null;
}

public sealed class ConnectivityProbe(AppSettings settings, SqlStagingRepository staging)
{
    public async Task<EnvironmentStatus> CheckAppDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(settings.AppConnectionString) { ConnectTimeout = 5 };
            await using (var connection = new SqlConnection(builder.ConnectionString))
                await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return new EnvironmentStatus(false, [], ex.Message);
        }

        try
        {
            await using var db = LogsDbContextFactory.Create(settings.AppConnectionString);
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
                return new EnvironmentStatus(true, pending, null);

            await staging.EnsureSchemaAsync(cancellationToken);
            return new EnvironmentStatus(true, [], null);
        }
        catch (Exception ex)
        {
            return new EnvironmentStatus(true, [], ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> CheckSourceDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(settings.SourceConnectionString) { ConnectTimeout = 5 };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return (true, $"{builder.DataSource} / {builder.InitialCatalog}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> CheckReceiverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = settings.ReceiverBaseUri, Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync("api/personnel", cancellationToken);
            return (true, $"{settings.ReceiverBaseUri} (HTTP {(int)response.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
