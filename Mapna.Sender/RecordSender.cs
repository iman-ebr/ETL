using Mapna.Contracts;
using Mapna.LogData;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using System.Text;

namespace Mapna.Sender;

public class RecordSender
{
    private readonly HttpClient _httpClient;
    private readonly LogDbContext _LogDbContext;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public RecordSender(HttpClient httpClient,LogDbContext logDbContext)
    {
        _httpClient = httpClient;
        _LogDbContext = logDbContext;

        _retryPolicy = Policy.HandleResult<HttpResponseMessage>(result=>!result.IsSuccessStatusCode).Or<HttpRequestException>()
            .WaitAndRetryAsync(retryCount:3, sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }

    public async Task<(SendStatus status,string? reason)> SendAsync(PersonnelRecord record, SendDecision decision)
    {
        switch (decision.Action)
        {
            case SendAction.SkipValidationFailed:
                await LogAsync(record.PerId, SendStatus.ValidationFailed, decision.Reason, decision.ChangedFields,
                    decision.PayloadSnapshot);
                break;
            case SendAction.SkipDuplicate:
                await LogAsync(record.PerId, SendStatus.Duplicate, null, null,
                    decision.PayloadSnapshot);
                break;
        }

        try
        {
            var json = JsonConvert.SerializeObject(record);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _retryPolicy.ExecuteAsync(() => _httpClient.PostAsync("api/personnel", content));
            if (response.IsSuccessStatusCode)
            {
                await LogAsync(record.PerId, SendStatus.Sent, null, decision.ChangedFields, decision.PayloadSnapshot);
                return (SendStatus.Sent, decision.ChangedFields);
            }

            var reason = $"Api Couldn't respond: {(int)response.StatusCode}";
            await LogAsync(record.PerId, SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
            return (SendStatus.SendFailed, reason);
        }
        catch (Exception ex)
        {
            var reason = $"Network error after retrying: {ex.Message}";
            await LogAsync(record.PerId, SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
            return (SendStatus.SendFailed, reason);
        }
    }

    private async Task LogAsync(int perId,SendStatus status,string? reason,string? changedField,string payLoadSnapShot)
    {
        _LogDbContext.SendLogs.Add(new SendLogEntry
        {
            PerId = perId,
            Status = status,
            Reason = reason,
            ChangedFields = changedField,
            PayloadSnapshot = payLoadSnapShot
        });
        await _LogDbContext.SaveChangesAsync();
    }

}