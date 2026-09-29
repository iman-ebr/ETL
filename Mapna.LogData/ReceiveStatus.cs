namespace Mapna.LogData;

// Stored as strings. Only ever ADD members; renaming one makes existing rows unreadable.
public enum ReceiveStatus
{
    Inserted,
    Updated,
    Duplicate,
    ValidationFailed,
    NationalCodeConflictWarning,     // legacy rows only; no longer written
    RejectedNationalCodeConflict
}
