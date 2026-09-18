using Microsoft.EntityFrameworkCore;

namespace Mapna.LogData;

public class LogsDbContextFactory
{
    public static LogDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<LogDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))
            .Options;

        return new LogDbContext(options);
    }
}