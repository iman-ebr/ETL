using Mapna.LogData;

namespace Mapna.Sender;

public enum SyncPhase
{
    Preparing,
    LoadingSource,
    Staging,
    Sending,
    Finalizing
}

public enum SyncPauseKind
{
    None,
    User,
    Connectivity,
    Throttled
}

public class SyncProgress
{
    public SyncPhase Phase { get; set; } = SyncPhase.Sending;
    public int Total { get; set; }
    public int Processed { get; set; }
    public int SentCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public string CurrentPerson { get; set; } = string.Empty;
    public int CurrentPerId { get; set; }

    /// <summary>
    /// Non-null only when this report is the final outcome of one record. It used to be a non-nullable enum
    /// that defaulted to Sent, so every "paused"/"resumed" report showed up in the grid as a phantom "Sent" row
    /// for a record that had NOT been sent yet.
    /// </summary>
    public SendStatus? LastStatus { get; set; }
    public string? LastReason { get; set; }
    public string? LastChangedFields { get; set; }
    public string? LastPayloadSnapshot { get; set; }
    public Guid? LastCorrelationId { get; set; }

    public bool IsRecordResult => LastStatus is not null;

    public bool IsPaused { get; set; }
    public SyncPauseKind PauseKind { get; set; }
    public string? PauseMessage { get; set; }
    public Guid? RunId { get; set; }
}
