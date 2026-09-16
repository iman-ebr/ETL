using Mapna.LogData;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System.Diagnostics;

namespace Mapna.Sender;

public class SyncOrchestrator
{
    private readonly AppSettings _settings;
    private const string ReceiverApiClientName = "ReceiverApi";

    private const int CheckpointBatchSize = 20;
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(3);

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

        using var serviceProvider = BuildHttpServices();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var httpclient = httpClientFactory.CreateClient(ReceiverApiClientName);
        httpclient.BaseAddress = new Uri(_settings.ReceiverApiBaseUrl);

        if (!string.IsNullOrWhiteSpace(_settings.ReceiverApiKey))
        {
            httpclient.DefaultRequestHeaders.Add("X-Api-Key", _settings.ReceiverApiKey);
        }

        var sender = new RecordSender(httpclient, logdb);
        var totalCount = records.Count;
        int processedCount = 0, sentCount = 0, duplicateCount = 0, failedCount = 0;

        var unsavedCount = 0;
        var checkpointStopwatch = Stopwatch.StartNew();

        try
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                    reason = $"Unexpected error while processing data: {ex.Message}";
                }

                unsavedCount++;

                var timeThresholdReached = checkpointStopwatch.Elapsed >= CheckpointInterval;
                if (unsavedCount >= CheckpointBatchSize || timeThresholdReached)
                {
                    await logdb.SaveChangesAsync(CancellationToken.None);
                    unsavedCount = 0;
                    checkpointStopwatch.Restart();
                }

                processedCount++;
                switch (status)
                {
                    case SendStatus.Sent: sentCount++; break;
                    case SendStatus.Duplicate: duplicateCount++; break;
                    case SendStatus.ValidationFailed:
                    case SendStatus.SendFailed: failedCount++; break;
                }

                progress.Report(new SyncProgress
                {
                    Total = totalCount,
                    Processed = processedCount,
                    SentCount = sentCount,
                    DuplicateCount = duplicateCount,
                    FailedCount = failedCount,
                    CurrentPerson = $"{record.PerName} {record.PerSurname}",
                    CurrentPerId = record.PerId,
                    LastStatus = status,
                    LastReason = reason
                });
            }
        }
        finally
        {
            if (logdb.ChangeTracker.HasChanges())
            {
                await logdb.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    private static ServiceProvider BuildHttpServices()
    {
        var services = new ServiceCollection();

        services.AddHttpClient(ReceiverApiClientName)
            .AddPolicyHandler(GetResiliencePolicy());

        return services.BuildServiceProvider();
    }

    private static IAsyncPolicy<HttpResponseMessage> GetResiliencePolicy()
    {
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }
}