using Mapna.Contracts;
using Mapna.LogData;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Mapna.Sender;

public class SendDecisionService
{
    private readonly PersonnelValidator _validator;
    private readonly IReadOnlyDictionary<int, SendState> _lastSentByPerId;

    public SendDecisionService(IReadOnlyDictionary<int, SendState> lastSentByPerId)
    {
        _validator = new PersonnelValidator();
        _lastSentByPerId = lastSentByPerId;
    }

    /// <summary>
    /// Read-only snapshot of the decision state as it was when the run started. AsNoTracking matters: the old
    /// code loaded every SendState as a TRACKED entity and mutated it later. The first flush called
    /// ChangeTracker.Clear(), which detached all of them, so every later state update was silently dropped.
    /// State is now written only by dbo.usp_FlushSyncResults, never through the change tracker.
    /// </summary>
    public static async Task<Dictionary<int, SendState>> LoadStatesAsync(LogDbContext logsDb, CancellationToken cancellationToken)
    {
        var states = await logsDb.SendStates.AsNoTracking().ToListAsync(cancellationToken);
        return states.ToDictionary(x => x.PerId);
    }

    public SendDecision Decide(PersonnelRecord record)
    {
        var currentSnapshot = JsonConvert.SerializeObject(record);

        var validationResult = _validator.Validate(record);
        if (!validationResult.IsValid)
        {
            var reasons = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return new SendDecision
            {
                Action = SendAction.SkipValidationFailed,
                Reason = reasons,
                PayloadSnapshot = currentSnapshot
            };
        }

        if (!_lastSentByPerId.TryGetValue(record.PerId, out var lastSent) ||
            string.IsNullOrEmpty(lastSent.PayloadSnapshot))
        {
            return new SendDecision { Action = SendAction.Send, PayloadSnapshot = currentSnapshot };
        }

        PersonnelRecord? previousRecord;
        try
        {
            previousRecord = JsonConvert.DeserializeObject<PersonnelRecord>(lastSent.PayloadSnapshot);
        }
        catch (JsonException)
        {
            previousRecord = null;
        }

        if (previousRecord is null)
        {
            return new SendDecision { Action = SendAction.Send, PayloadSnapshot = currentSnapshot };
        }

        var changedFields = FieldChangeDetector.GetChangedField(record, previousRecord, nameof(PersonnelRecord.PerId));

        if (changedFields.Count == 0)
        {
            return new SendDecision { Action = SendAction.SkipDuplicate, PayloadSnapshot = currentSnapshot };
        }

        return new SendDecision
        {
            Action = SendAction.Send,
            ChangedFields = string.Join(",", changedFields),
            PayloadSnapshot = currentSnapshot
        };
    }
}
