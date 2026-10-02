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

    public string? PlayLoadHash { get; set; }

    public Guid? RunId { get; set; }

    public Guid? CorrelationId { get; set; }
}
