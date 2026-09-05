using Mapna.Contracts;
using Mapna.LogData;
using Newtonsoft.Json;

namespace Mapna.Sender;

public class SendDecisionService
{
    private readonly LogDbContext _logsDb;
    private readonly PersonnelValidator _validator;

    public SendDecisionService(LogDbContext logsDb)
    {
        _logsDb = logsDb;
        _validator = new PersonnelValidator();
    }

    public SendDecision Decide(PersonnelRecord record)
    {
        var validationResult = _validator.Validate(record);
        if (!validationResult.IsValid)
        {
            var reasons = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return new SendDecision
            {
                Action = SendAction.SkipValidationFailed,
                Reason = reasons,
                PayloadSnapshot = JsonConvert.SerializeObject(record)
            };
        }

        var currentSnapshot = JsonConvert.SerializeObject(record);

        var lastSent = _logsDb.SendLogs
            .Where(x => x.PerId == record.PerId && x.Status == SendStatus.Sent)
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstOrDefault();

        if (lastSent is null || string.IsNullOrEmpty(lastSent.PayloadSnapshot))
        {
            return new SendDecision
            {
                Action = SendAction.Send,
                PayloadSnapshot = currentSnapshot
            };
        }

        var previousRecord = JsonConvert.DeserializeObject<PersonnelRecord>(lastSent.PayloadSnapshot);
        var changedFields = FieldChangeDetector.GetChangedField(
            record, previousRecord!, nameof(PersonnelRecord.PerId));

        if (changedFields.Count == 0)
        {
            return new SendDecision
            {
                Action = SendAction.SkipDuplicate,
                PayloadSnapshot = currentSnapshot
            };
        }

        return new SendDecision
        {
            Action = SendAction.Send,
            ChangedFields = string.Join(",", changedFields),
            PayloadSnapshot = currentSnapshot
        };
    }
}
