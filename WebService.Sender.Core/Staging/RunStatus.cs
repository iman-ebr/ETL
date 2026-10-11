namespace WebService.Sender.Staging;

public enum RunStatus
{
    Running,
    Paused,
    Completed,
    CompletedWithFailures,
    Cancelled,
    Crashed
}