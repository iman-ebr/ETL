using Mapna.Contracts;
using Mapna.LogData;
using Newtonsoft.Json;
using System.Text;

namespace Mapna.Sender;

public class RecordSender
{
    private readonly HttpClient _httpClient;
    private readonly LogDbContext _logDbContext;

    public RecordSender(HttpClient httpClient, LogDbContext logDbContext)
    {
        _httpClient = httpClient;
        _logDbContext = logDbContext;
    }

    public async Task<(SendStatus status, string? reason)> SendAsync(
        PersonnelRecord record, SendDecision decision, CancellationToken cancellationToken)
    {
        if (decision.Action == SendAction.SkipValidationFailed)
        {
            AddLog(record.PerId, SendStatus.ValidationFailed, decision.Reason, decision.ChangedFields, decision.PayloadSnapshot);
            return (SendStatus.ValidationFailed, decision.Reason);
        }

        if (decision.Action == SendAction.SkipDuplicate)
        {
            AddLog(record.PerId, SendStatus.Duplicate, null, null, decision.PayloadSnapshot);
            return (SendStatus.Duplicate, null);
        }

        try
        {
            var json = JsonConvert.SerializeObject(record);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("api/personnel", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                AddLog(record.PerId, SendStatus.Sent, null, decision.ChangedFields, decision.PayloadSnapshot);
                return (SendStatus.Sent, decision.ChangedFields);
            }

            var body = await SafeReadBodyAsync(response, cancellationToken);
            var reason = $"Api responded with {(int)response.StatusCode}: {body}";
            AddLog(record.PerId, SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
            return (SendStatus.SendFailed, reason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = $"Network error after retrying: {ex.Message}";
            AddLog(record.PerId, SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
            return (SendStatus.SendFailed, reason);
        }
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void AddLog(int perId, SendStatus status, string? reason, string? changedField, string payloadSnapshot)
    {
        _logDbContext.SendLogs.Add(new SendLogEntry
        {
            PerId = perId,
            OccurredAtUtc = DateTime.UtcNow,
            Status = status,
            Reason = LogFieldLimit.Truncate(reason, LogFieldLimit.ReasonMaxLength),
            ChangedFields = LogFieldLimit.Truncate(changedField, LogFieldLimit.ChangedFieldsMaxLength),
            PayloadSnapshot = payloadSnapshot
        });
    }
}