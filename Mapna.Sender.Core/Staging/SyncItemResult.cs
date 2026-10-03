using Mapna.LogData;

namespace Mapna.Sender.Staging;

/// <summary>One record's final outcome in a run. Persisted atomically to SendLogs + SendStates + SyncItems.</summary>
public sealed class SyncItemResult
{
    public int PerId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public SendStatus Status { get; init; }
    public string? Reason { get; init; }
    public string? ChangedFields { get; init; }
    public string? PayloadSnapshot { get; init; }
    public bool ConfirmedByReceiver { get; init; }

    /// <summary>Idempotency key of the audit row. Flushing the same result twice (after an ambiguous commit) inserts one row.</summary>
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
