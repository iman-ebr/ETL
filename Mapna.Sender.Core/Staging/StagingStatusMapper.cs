using Mapna.LogData;

namespace Mapna.Sender.Staging;


public static class StagingStatusMapper
{
    public static StagingItemStatus FromSendStatus(SendStatus status) => status switch
    {
        SendStatus.Sent => StagingItemStatus.Sent,
        SendStatus.Duplicate => StagingItemStatus.Duplicate,
        SendStatus.ValidationFailed => StagingItemStatus.ValidationFailed,
        SendStatus.SendFailed => StagingItemStatus.SendFailed,
        _ => StagingItemStatus.SendFailed
    };

    public static SendStatus ToSendStatus(StagingItemStatus status) => status switch
    {
        StagingItemStatus.Sent => SendStatus.Sent,
        StagingItemStatus.Duplicate => SendStatus.Duplicate,
        StagingItemStatus.ValidationFailed => SendStatus.ValidationFailed,
        _ => SendStatus.SendFailed
    };

    public static bool IsFinal(StagingItemStatus status) => status != StagingItemStatus.Pending;
}