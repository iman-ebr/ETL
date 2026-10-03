using Microsoft.Extensions.Configuration;

namespace Mapna.Sender;

public class AppSettings
{
    public string SourceConnectionString { get; set; } = string.Empty;
    public string AppConnectionString { get; set; } = string.Empty;
    public string ReceiverApiBaseUrl { get; set; } = string.Empty;
    public string? ReceiverApiKey { get; set; }
    public string LogsDirectory { get; set; } = "logs";

    public bool NormalizeArabicLetters { get; set; }

    public static AppSettings Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        return FromConfiguration(config);
    }

    public static AppSettings FromConfiguration(IConfiguration config) => new()
    {
        SourceConnectionString = config["SourceDatabase:ConnectionString"] ?? string.Empty,
        AppConnectionString = config["AppDatabase:ConnectionString"] ?? string.Empty,
        ReceiverApiBaseUrl = config["ReceiverApi:BaseUrl"] ?? string.Empty,
        ReceiverApiKey = config["ReceiverApi:ApiKey"],
        LogsDirectory = ResolvePath(config["Logging:Directory"] ?? "logs"),
        NormalizeArabicLetters = config.GetValue("Sync:NormalizeArabicLetters", false)
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SourceConnectionString)) errors.Add("SourceDatabase:ConnectionString خالی است.");
        if (string.IsNullOrWhiteSpace(AppConnectionString)) errors.Add("AppDatabase:ConnectionString خالی است.");
        if (!Uri.TryCreate(ReceiverApiBaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            errors.Add("ReceiverApi:BaseUrl یک آدرس http/https معتبر نیست.");
        if (string.IsNullOrWhiteSpace(ReceiverApiKey) || ReceiverApiKey.StartsWith("CHANGE-ME", StringComparison.OrdinalIgnoreCase))
            errors.Add("ReceiverApi:ApiKey تنظیم نشده است.");
        return errors;
    }

    public Uri ReceiverBaseUri => new(ReceiverApiBaseUrl.EndsWith('/') ? ReceiverApiBaseUrl : ReceiverApiBaseUrl + "/");

    private static string ResolvePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);
}
