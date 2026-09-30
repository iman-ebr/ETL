using Microsoft.Data.SqlClient;

namespace Mapna.Sender;

public static class SqlTransientErrors
{
    private static readonly HashSet<int> TransientNumbers =
    [
        -2,     
        20, 64, 233, 10053, 10054, 10060, 10061, 11001,   
        121, 258,                                        
        1205,  
        1222,   
        4060,   
        4221,  
        10928, 10929, 40197, 40501, 40613, 40143, 49918, 49919, 49920 
    ];

    public static bool IsTransient(Exception ex) => ex switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(e => TransientNumbers.Contains(e.Number)),
        TimeoutException => true,
        _ => false
    };
}
