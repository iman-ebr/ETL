namespace Mapna.Sender.Staging;

public class StagingItem
{
    public Guid RunId { get; set; }
    public int PerId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public StagingItemStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? ChangedFields { get; set; }
    public string? PayloadSnapshot { get; set; }
    public int AttemptCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

}