namespace Mapna.LogData;

public class SendLogEntry
{
    public int Id { get; set; }
    public int PerId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public SendStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? ChangedFields { get; set; }
    public string? PayloadSnapshot { get; set; }
}