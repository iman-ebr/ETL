using System.Runtime.CompilerServices;
using Mapna.LogData;

namespace Mapna.Sender;

public class SyncOrchestrator
{
    private readonly AppSettings _settings;

    public SyncOrchestrator(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task RunAsync(IProgress<SyncProgress> progress, CancellationToken cancellationToken)
    {
        var srcRepo = new SourceRepository(_settings.SourceConnectionString);
        var records = srcRepo.GetAllPersonnel();
        await using var logdb = LogsDbContextFactory.Create(_settings.AppConnectionString);
        var decisionService = new SendDecisionService(logdb);
        using var httpclient = new HttpClient()
        {
            BaseAddress = new Uri(_settings.ReceiverApiBaseUrl)
        };
        var sender = new RecordSender(httpclient, logdb);
        var report = new SyncProgress { Total = records.Count };

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            report.CurrentPerson = $"{record.PerName} {record.PerSurname}";
            var decision = decisionService.Decide(record);
            var(status , reason) = await sender.SendAsync(record, decision);

            report.Processed++;
            report.CurrentPerId = record.PerId;
            report.LastStatus = status;
            report.LastReason = reason;

            switch (decision.Action)
            {
                case SendAction.Send:
                    report.SentCount++;
                    break;
                case SendAction.SkipDuplicate:
                    report.DuplicateCount++;
                    break;
                case SendAction.SkipValidationFailed:
                    report.FailedCount++;
                    break;  
            }

            progress.Report(report);
        }

    }
}