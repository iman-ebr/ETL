using WebService.LogData;

namespace WebService.Sender.Staging;

public sealed class SyncItemResult
{
    public int PerId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public SendStatus Status { get; init; }
    public string? Reason { get; init; }
    public string? ChangedFields { get; init; }
    public string? PayloadSnapshot { get; init; }
    public bool ConfirmedByReceiver { get; init; }

    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
