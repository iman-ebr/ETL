namespace Mapna.LogData;

public class SendLogEntry
{
    public long Id { get; set; }
    public int PerId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public SendStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? ChangedFields { get; set; }
    public string? PayloadSnapshot { get; set; }

    // Never written. Kept because renaming a property makes EF generate DROP + ADD (data loss).
    // Use migrationBuilder.RenameColumn if you ever need to rename it.
    public string? PlayLoadHash { get; set; }

    public Guid? RunId { get; set; }

    /// <summary>Sent as X-Correlation-Id and stored in ReceiveLogs too, so one send can be traced end to end.
    /// Also the idempotency key for the audit insert (unique index), so replaying a flush can't duplicate rows.</summary>
    public Guid? CorrelationId { get; set; }
}
