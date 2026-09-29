using Microsoft.Data.SqlClient;

namespace Mapna.Sender;

/// <summary>
/// The old policies retried on ANY SqlException/DbUpdateException, then fed them into a circuit breaker whose
/// "open" state the orchestrator answered with "wait 32s and try again", forever. A deterministic error
/// (string truncation, PK/unique violation, missing column) therefore became an infinite pause loop. Only
/// errors that can succeed on retry are transient; everything else must fail fast and loudly.
/// </summary>
public static class SqlTransientErrors
{
    private static readonly HashSet<int> TransientNumbers =
    [
        -2,     // client timeout
        20, 64, 233, 10053, 10054, 10060, 10061, 11001,   // connection / network
        121, 258,                                         // semaphore timeout / wait timeout
        1205,   // deadlock victim
        1222,   // lock request timeout
        4060,   // cannot open database (failover / restore)
        4221,   // login timeout during failover (AG)
        10928, 10929, 40197, 40501, 40613, 40143, 49918, 49919, 49920   // Azure SQL throttling / reconfiguration
    ];

    public static bool IsTransient(Exception ex) => ex switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(e => TransientNumbers.Contains(e.Number)),
        TimeoutException => true,
        _ => false
    };
}
