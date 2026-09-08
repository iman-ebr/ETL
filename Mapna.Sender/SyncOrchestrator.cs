using Mapna.LogData;

namespace Mapna.Sender;

public class SyncOrchestrator
{
    private readonly AppSettings _settings;
    private const int saveBatchSize = 200;
    private const string ReceiverApiClientName = "ReceiverApi";


    public SyncOrchestrator(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task RunAsync(IProgress<SyncProgress> progress, CancellationToken cancellationToken)
    {
        var srcRepo = new SourceRepository(_settings.SourceConnectionString);
        var records = srcRepo.GetAllPersonnel();
        await using var logdb = LogsDbContextFactory.Create(_settings.AppConnectionString);
        var lastSentByPerId = await SendDecisionService.LoadLastSentAsync(logdb, cancellationToken);
        var decisionService = new SendDecisionService(lastSentByPerId);

        using var httpclient = new HttpClient();
        httpclient.BaseAddress = new Uri(_settings.ReceiverApiBaseUrl);

        if (!string.IsNullOrWhiteSpace(_settings.ReceiverApiKey))
        {
            httpclient.DefaultRequestHeaders.Add("X-Api-Key", _settings.ReceiverApiKey);
        }


        var sender = new RecordSender(httpclient, logdb);
        var report = new SyncProgress { Total = records.Count };
        var unsavedCount = 0;

        try
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                report.CurrentPerson = $"{record.PerName} {record.PerSurname}";
                report.CurrentPerId = record.PerId;
                SendStatus status;
                string? reason;
                try
                {
                    var decision = decisionService.Decide(record);
                    (status, reason) = await sender.SendAsync(record, decision, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    status = SendStatus.SendFailed;
                    reason = $"خطای غیرمنتظره در پردازش رکورد: {ex.Message}";
                }

                report.Processed++;
                // report.CurrentPerId = record.PerId;
                report.LastStatus = status;
                report.LastReason = reason;

                switch (status)
                {
                    case SendStatus.Sent:
                        report.SentCount++;
                        break;
                    case SendStatus.Duplicate:
                        report.DuplicateCount++;
                        break;
                    case SendStatus.ValidationFailed:
                    case SendStatus.SendFailed:
                        report.FailedCount++;
                        break;
                }

                progress.Report(report);
                unsavedCount++;
                if (unsavedCount < saveBatchSize) continue;
                await logdb.SaveChangesAsync(CancellationToken.None);
                unsavedCount = 0;

            }
        }
        finally
        {
            if (logdb.ChangeTracker.HasChanges())
                await logdb.SaveChangesAsync(CancellationToken.None);
        }
    }
    // private static ServiceProvider BuildHttpServices()
    // {
    //     var services = new ServiceCollection();
    //
    //     services.AddHttpClient(ReceiverApiClientName)
    //         .AddPolicyHandler(GetRetryPolicy());
    //
    //     return services.BuildServiceProvider();
    // }
    //
    // private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    // {
    //     return HttpPolicyExtensions
    //         .HandleTransientHttpError()
    //         .WaitAndRetryAsync(
    //             retryCount: 3,
    //             sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    // }
}