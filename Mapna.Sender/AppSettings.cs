using Microsoft.Extensions.Configuration;

namespace Mapna.Sender;

public class AppSettings
{
    public string SourceConnectionString { get; set; } = string.Empty;
    public string AppConnectionString { get; set; } = string.Empty;
    public string ReceiverApiBaseUrl { get; set; } = string.Empty;

    public static AppSettings Load()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        return new AppSettings
        {
            SourceConnectionString = config["SourceDatabase:ConnectionString"] ?? string.Empty,
            AppConnectionString = config["AppDatabase:ConnectionString"] ?? string.Empty,
            ReceiverApiBaseUrl = config["ReceiverApi:BaseUrl"] ?? string.Empty
        };
    }
}
