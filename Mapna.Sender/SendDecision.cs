namespace Mapna.Sender;

public class SendDecision
{
    public SendAction Action { get; set; }
    public string? Reason { get; set; }
    public string? ChangedFields { get; set; }
    public string PayloadSnapshot { get; set; } = string.Empty;
}

public enum SendAction
{
    Send,
    SkipDuplicate,
    SkipValidationFailed
}
