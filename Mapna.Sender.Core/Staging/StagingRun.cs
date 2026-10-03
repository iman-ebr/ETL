namespace Mapna.Sender.Staging;

public class StagingRun
{
    public Guid RunId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public RunStatus Status { get; set; }
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public int SentCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public string MachineName { get; set; } = Environment.MachineName;
    public string? StopReason { get; set; }

}