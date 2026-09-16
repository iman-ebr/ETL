using Mapna.LogData;

namespace Mapna.Sender;

public class RecordResult
{
    public int PerId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public SendStatus Status { get; set; }
    public string? Reason { get; set; }
}