namespace Mapna.LogData;

/// <summary>
/// Decision state: exactly one row per PerId. It is NOT an audit log and must never be touched by the
/// SendLogs cleanup job. Written only through dbo.usp_FlushSyncResults (see SqlStagingRepository), in the
/// same transaction as the matching SendLogs row, so the two can never disagree.
/// </summary>
public class SendState
{
    public int PerId { get; set; }

    /// <summary>The last payload the receiver confirmed with a 2xx. Null means we never had a confirmed send.</summary>
    public string? PayloadSnapshot { get; set; }

    public DateTime? LastSentAtUtc { get; set; }
    public SendStatus LastStatus { get; set; }
    public DateTime LastAttemptAtUtc { get; set; }
}
