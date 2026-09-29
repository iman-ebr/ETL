namespace Mapna.LogData;

public class SendState
{
    public int PerId { get; set; }
    public string PayloadSnapshot { get; set; } = string.Empty;
    public DateTime LastSentAtUtc { get; set; }
    public SendStatus LastStatus { get; set; }
    public DateTime LastAttemptAtUtc { get; set; }
}