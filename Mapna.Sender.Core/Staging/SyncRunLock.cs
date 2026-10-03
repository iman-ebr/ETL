using System.Data;
using Microsoft.Data.SqlClient;

namespace Mapna.Sender.Staging;

public sealed class RunLockLostException()
    : Exception("اتصال نگه‌دارنده‌ی قفل اجرا به پایگاه‌داده قطع شد؛ برای جلوگیری از اجرای هم‌زمان، اجرا متوقف شد و قابل ادامه است.");


public sealed class SyncRunLock : IAsyncDisposable
{
    public const string Resource = "Mapna.Sender.SyncRun";
    private readonly SqlConnection _connection;

    private SyncRunLock(SqlConnection connection) => _connection = connection;

    public static async Task<SyncRunLock?> TryAcquireAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { Pooling = false, ApplicationName = "Mapna.Sender.RunLock" };
        var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "sp_getapplock";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Resource", Resource);
            cmd.Parameters.AddWithValue("@LockMode", "Exclusive");
            cmd.Parameters.AddWithValue("@LockOwner", "Session");
            cmd.Parameters.AddWithValue("@LockTimeout", 0);
            cmd.Parameters.AddWithValue("@DbPrincipal", "public");
            var ret = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
            ret.Direction = ParameterDirection.ReturnValue;
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            var result = (int)ret.Value!;
            if (result >= 0)
                return new SyncRunLock(connection);

            await connection.DisposeAsync();
            return result == -1 ? null : throw new InvalidOperationException($"sp_getapplock returned {result}");
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task EnsureHeldAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT APPLOCK_MODE('public', @Resource, 'Session');";
            cmd.Parameters.AddWithValue("@Resource", Resource);
            var mode = (string?)await cmd.ExecuteScalarAsync(cancellationToken);
            if (mode != "Exclusive") throw new RunLockLostException();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw new RunLockLostException();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_connection.State == ConnectionState.Open)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = "EXEC sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session', @DbPrincipal = 'public';";
                cmd.Parameters.AddWithValue("@Resource", Resource);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch
        {
        }
        await _connection.DisposeAsync();
    }
}
