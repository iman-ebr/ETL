using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Mapna.LogData;

public class LogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LogDbContext>
{
    private const string FallbackConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=MapnaEtlDb;Trusted_Connection=True;TrustServerCertificate=True;";

    public LogDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionstring = configuration.GetConnectionString("AppDataBase") ??
                               configuration["AppDataBase:ConnectionString"]
                               ?? FallbackConnectionString;

        var optionbuilder = new DbContextOptionsBuilder<LogDbContext>();
        optionbuilder.UseSqlServer(connectionstring);
        return new LogDbContext(optionbuilder.Options);

    }
}