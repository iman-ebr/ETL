using Mapna.Contracts;
using Mapna.LogData;
using Microsoft.EntityFrameworkCore;

namespace Mapna.Receiver;

public class PesonnelUpsertService
{
    private readonly LogDbContext _db;
    private readonly PersonnelValidator validator;

    public PesonnelUpsertService(LogDbContext db)
    {
        _db = db;
        validator = new PersonnelValidator();
    }

    public async Task<ReceiveStatus> ProcessAsync(PersonnelRecord record)
    {
        var validationResult = validator.Validate(record);
        if (!validationResult.IsValid)
        {
            var reasons = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            await AddLogAsync(record.PerId, ReceiveStatus.ValidationFailed, null, reasons);
            await _db.SaveChangesAsync();
            return ReceiveStatus.ValidationFailed;
        }
        await CheckNationalCodeConflictAsync(record);

        var existing = await _db.Personnel
            .FirstOrDefaultAsync(p => p.PerId == record.PerId);

        if (existing is null)
        {
            var newEntity = new Personnel();
            record.ApplyTo(newEntity);
            _db.Personnel.Add(newEntity);

            await AddLogAsync(record.PerId, ReceiveStatus.Inserted, null, null);
            await _db.SaveChangesAsync();
            return ReceiveStatus.Inserted;
        }

        var changedFields = FieldChangeDetector.GetChangedField(
            record, existing, nameof(PersonnelRecord.PerId));

        if (changedFields.Count == 0)
        {
            await AddLogAsync(record.PerId, ReceiveStatus.Duplicate, null, null);
            await _db.SaveChangesAsync();
            return ReceiveStatus.Duplicate;
        }

        record.ApplyTo(existing);
        var changedFieldsText = string.Join(",", changedFields);

        await AddLogAsync(record.PerId, ReceiveStatus.Updated, changedFieldsText, null);
        await _db.SaveChangesAsync();
        return ReceiveStatus.Updated;
    }

    private async Task CheckNationalCodeConflictAsync(PersonnelRecord record)
    {
        var conflict = await _db.Personnel
            .AnyAsync(p => p.NationalCode == record.NationalCode && p.PerId != record.PerId);

        if (conflict)
        {
            await AddLogAsync(
                record.PerId,
                ReceiveStatus.NationalCodeConflictWarning,
                null,
                $"کد ملی {record.NationalCode} قبلاً برای یک PerId دیگر ثبت شده است.");
        }
    }
    



    private Task AddLogAsync(int perId, ReceiveStatus status, string? changedFields, string? reason)
    {
        _db.ReceiveLogs.Add(new ReceiveLogEntry
        {
            PerId = perId,
            OccurredAtUtc = DateTime.UtcNow,
            Status = status,
            ChangedFields = changedFields,
            Reason = reason
        });

        return Task.CompletedTask;
    }

}