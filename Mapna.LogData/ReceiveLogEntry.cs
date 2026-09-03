using Mapna.LogData;

namespace Mapna.LogData;

public class ReceiveLogEntry
{
    public int Id { get; set; }
    public int PerId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public ReceiveStatus Status { get; set; }
    public string? ChangedFields { get; set; }
    public string? Reason { get; set; }
}