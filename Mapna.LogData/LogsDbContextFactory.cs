using Microsoft.EntityFrameworkCore;

namespace Mapna.LogData;

public class LogsDbContextFactory
{
    public static LogDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<LogDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new LogDbContext(options);
    }
}
