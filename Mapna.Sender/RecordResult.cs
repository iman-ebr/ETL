using Mapna.LogData;
using Mapna.Sender.Staging;

namespace Mapna.Sender;

public class RecordResult
{
    public int PerId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public SendStatus Status { get; set; }
    public string? Reason { get; set; }

    public string? ChangedFields { get; set; }
    public string? PayloadSnapshot { get; set; }
    public int AttemptCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public static RecordResult FromStagingItem(StagingItem item) => new()
    {
        PerId = item.PerId,
        PersonName = item.PersonName,
        Status = StagingStatusMapper.ToSendStatus(item.Status),
        Reason = item.Reason,
        ChangedFields = item.ChangedFields,
        PayloadSnapshot = item.PayloadSnapshot,
        AttemptCount = item.AttemptCount,
        UpdatedAtUtc = item.UpdatedAtUtc
    };
}