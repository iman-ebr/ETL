namespace WebService.Sender.Staging;

public sealed record OutstandingItem(
    int PerId,
    string PersonName,
    StagingItemStatus Status,
    string? Reason,
    int AttemptCount,
    DateTime UpdatedAtUtc);