using Mapna.Contracts;
using Mapna.LogData;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;

namespace Mapna.Sender;

public sealed record SendOutcome(SendStatus Status, string? Reason, string? ChangedFields, string PayloadSnapshot);

public class RecordSender
{
    private readonly HttpClient _httpClient;
    private readonly LogDbContext _logDbContext;
    private readonly Dictionary<int, SendState> _states;

    public RecordSender(HttpClient httpClient, LogDbContext logDbContext ,Dictionary<int, SendState> states)
    {
        _httpClient = httpClient;
        _logDbContext = logDbContext;
        _states = states;
    }

    public async Task<SendOutcome> SendAsync(
        PersonnelRecord record, SendDecision decision, CancellationToken cancellationToken)
    {
        if (decision.Action == SendAction.SkipValidationFailed)
        {
            AddAuditLog(record.PerId, SendStatus.ValidationFailed, decision.Reason, decision.ChangedFields, decision.PayloadSnapshot);
            return new SendOutcome(SendStatus.ValidationFailed, decision.Reason, decision.ChangedFields, decision.PayloadSnapshot);
        }

        if (decision.Action == SendAction.SkipDuplicate)
        {
            AddAuditLog(record.PerId, SendStatus.Duplicate, null, null, decision.PayloadSnapshot);
            return new SendOutcome(SendStatus.Duplicate, null, null, decision.PayloadSnapshot);
        }

        try
        {
            var json = JsonConvert.SerializeObject(record);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("api/personnel", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                AddAuditLog(record.PerId, SendStatus.Sent, null, decision.ChangedFields, decision.PayloadSnapshot);
                UpsertState(record.PerId, SendStatus.Sent, decision.PayloadSnapshot);
                return new SendOutcome(SendStatus.Sent, Reason: null, ChangedFields: decision.ChangedFields, PayloadSnapshot: decision.PayloadSnapshot);
            }

            var body = await SafeReadBodyAsync(response, cancellationToken);
            var reason = $"Api responded with {(int)response.StatusCode}: {body}";
            AddAuditLog(record.PerId, SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
            UpsertState(record.PerId, SendStatus.SendFailed, confirmedPayloadSnapshot: null);
            return new SendOutcome(SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = $"Network error after retrying: {ex.Message}";
            AddAuditLog(record.PerId, SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
            UpsertState(record.PerId, SendStatus.SendFailed, confirmedPayloadSnapshot: null);
            return new SendOutcome(SendStatus.SendFailed, reason, null, decision.PayloadSnapshot);
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

    private void AddAuditLog(int perId, SendStatus status, string? reason, string? changedField, string payloadSnapshot)
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

    private void UpsertState(int perId, SendStatus status, string? confirmedPayloadSnapshot)
    {
        var now = DateTime.UtcNow;

        if (!_states.TryGetValue(perId, out var state))
        {
            state = new SendState { PerId = perId };
            _logDbContext.SendStates.Add(state);
            _states[perId] = state;
        }

        state.LastStatus = status;
        state.LastAttemptAtUtc = now;

        if (confirmedPayloadSnapshot is null) return;
        state.PayloadSnapshot = confirmedPayloadSnapshot;
        state.LastSentAtUtc = now;
    }
}