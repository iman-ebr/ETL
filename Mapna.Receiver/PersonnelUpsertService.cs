using System.Data;
using Mapna.Contracts;
using Mapna.LogData;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Mapna.Receiver;

public sealed record UpsertResult(ReceiveStatus Status, string? ChangedFields = null, int? ConflictingPerId = null);

public class PersonnelUpsertService
{
    private readonly LogDbContext _db;
    private readonly PersonnelValidator _validator = new();

    public PersonnelUpsertService(LogDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Idempotent upsert. Sending the same payload twice (a retry after a timeout, a resend after a crash)
    /// yields Inserted/Updated then Duplicate, never a second write. Concurrent requests for the same PerId or
    /// the same NationalCode are serialized with key-range U-locks, so "read, compare, write" is atomic.
    /// </summary>
    public async Task<UpsertResult> ProcessAsync(PersonnelRecord record, Guid? correlationId)
    {
        var validationResult = _validator.Validate(record);
        if (!validationResult.IsValid)
        {
            var reasons = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            _db.ChangeTracker.Clear();
            AddLog(record.PerId, ReceiveStatus.ValidationFailed, null, reasons, correlationId);
            await _db.SaveChangesAsync();
            return new UpsertResult(ReceiveStatus.ValidationFailed);
        }

        // With EnableRetryOnFailure, a user transaction must run inside the execution strategy. If a transient
        // error hits *after* the commit reached the server, the whole block re-runs. The second pass finds the
        // row already written and returns Duplicate, so the retry is harmless.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            // UPDLOCK + HOLDLOCK = an update-intent key-range lock held to the end of the transaction. A second
            // request for the same PerId blocks here until we commit, then sees our result (no lost update and
            // no double insert). U-locks are incompatible with each other, so two same-key requests can't deadlock
            // the way two S-locks upgrading to X would.
            var existing = await _db.Personnel
                .FromSqlInterpolated($"SELECT * FROM dbo.Personnel WITH (UPDLOCK, HOLDLOCK) WHERE PerId = {record.PerId}")
                .SingleOrDefaultAsync();

            var conflictingPerId = await _db.Personnel
                .FromSqlInterpolated($"SELECT * FROM dbo.Personnel WITH (UPDLOCK, HOLDLOCK) WHERE NationalCode = {record.NationalCode}")
                .Where(p => p.PerId != record.PerId)
                .Select(p => (int?)p.PerId)
                .FirstOrDefaultAsync();

            if (conflictingPerId is not null)
            {
                // Two PerIds sharing one national code = the same human twice. Refuse; a person must decide
                // (re-hire with new PerId? typo in source?). UX_Personnel_NationalCode enforces the same rule
                // in the database, in case a future code path forgets this check.
                AddLog(record.PerId, ReceiveStatus.RejectedNationalCodeConflict, null,
                    $"کد ملی {record.NationalCode} متعلق به PerId {conflictingPerId} است؛ رکورد پذیرفته نشد.", correlationId);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return new UpsertResult(ReceiveStatus.RejectedNationalCodeConflict, ConflictingPerId: conflictingPerId);
            }

            if (existing is null)
            {
                var entity = new Personnel();
                record.ApplyTo(entity);
                _db.Personnel.Add(entity);
                AddLog(record.PerId, ReceiveStatus.Inserted, null, null, correlationId);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return new UpsertResult(ReceiveStatus.Inserted);
            }

            var changedFields = FieldChangeDetector.GetChangedField(record, existing, nameof(PersonnelRecord.PerId));
            if (changedFields.Count == 0)
            {
                AddLog(record.PerId, ReceiveStatus.Duplicate, null, null, correlationId);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return new UpsertResult(ReceiveStatus.Duplicate);
            }

            record.ApplyTo(existing);
            var changedFieldsText = string.Join(",", changedFields);
            AddLog(record.PerId, ReceiveStatus.Updated, changedFieldsText, null, correlationId);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return new UpsertResult(ReceiveStatus.Updated, changedFieldsText);
        });
    }

    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

    private void AddLog(int perId, ReceiveStatus status, string? changedFields, string? reason, Guid? correlationId)
    {
        _db.ReceiveLogs.Add(new ReceiveLogEntry
        {
            PerId = perId,
            OccurredAtUtc = DateTime.UtcNow,
            Status = status,
            ChangedFields = LogFieldLimit.Truncate(changedFields, LogFieldLimit.ChangedFieldsMaxLength),
            Reason = LogFieldLimit.Truncate(reason, LogFieldLimit.ReasonMaxLength),
            CorrelationId = correlationId
        });
    }
}
