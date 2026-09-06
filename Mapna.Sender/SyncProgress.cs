using Mapna.LogData;

namespace Mapna.Sender;

public class SyncProgress
{
    public int Total { get; set; }
    public int Processed { get; set; }
    public int SentCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public string CurrentPerson { get; set; } = string.Empty;
    public int CurrentPerId { get; set; }
    public SendStatus LastStatus { get; set; }
    public string? LastReason { get; set; }
}