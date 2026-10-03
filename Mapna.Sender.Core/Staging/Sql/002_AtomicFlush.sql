-- 002: atomic result flush (SendLogs + SendStates + SyncItems in ONE transaction).
-- Created automatically by SqlStagingRepository.EnsureSchemaAsync; kept here for DBA review.
-- Requires EF migration AddSendStatesAndAuditCorrelation (SendStates table, SendLogs.RunId/CorrelationId).

IF TYPE_ID(N'dbo.SyncResultTableType') IS NULL
BEGIN
    CREATE TYPE dbo.SyncResultTableType AS TABLE
    (
        PerId               INT              NOT NULL PRIMARY KEY,
        PersonName          NVARCHAR(200)    NULL,
        Status              NVARCHAR(30)     NOT NULL,
        Reason              NVARCHAR(500)    NULL,
        ChangedFields       NVARCHAR(500)    NULL,
        PayloadSnapshot     NVARCHAR(MAX)    NULL,
        ConfirmedByReceiver BIT              NOT NULL,
        CorrelationId       UNIQUEIDENTIFIER NOT NULL,
        OccurredAtUtc       DATETIME2(7)     NOT NULL
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_FlushSyncResults
    @RunId UNIQUEIDENTIFIER,
    @Items dbo.SyncResultTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    -- 1) Audit trail. Idempotent on CorrelationId (unique filtered index IX_SendLogs_CorrelationId).
    INSERT INTO dbo.SendLogs (PerId, OccurredAtUtc, Status, Reason, ChangedFields, PayloadSnapshot, RunId, CorrelationId)
    SELECT i.PerId, i.OccurredAtUtc, i.Status, i.Reason, i.ChangedFields, i.PayloadSnapshot, @RunId, i.CorrelationId
    FROM @Items i
    WHERE NOT EXISTS (SELECT 1 FROM dbo.SendLogs l WHERE l.CorrelationId = i.CorrelationId);

    -- 2) Decision state. Only a receiver-confirmed send may move the snapshot forward.
    --    Duplicate / ValidationFailed leave the state exactly as it was (same as before the refactor).
    MERGE dbo.SendStates WITH (HOLDLOCK) AS t
    USING (SELECT * FROM @Items WHERE Status IN (N'Sent', N'SendFailed')) AS s
        ON t.PerId = s.PerId
    WHEN MATCHED THEN UPDATE SET
        t.LastStatus       = s.Status,
        t.LastAttemptAtUtc = SYSUTCDATETIME(),
        t.PayloadSnapshot  = CASE WHEN s.ConfirmedByReceiver = 1 THEN s.PayloadSnapshot ELSE t.PayloadSnapshot END,
        t.LastSentAtUtc    = CASE WHEN s.ConfirmedByReceiver = 1 THEN SYSUTCDATETIME() ELSE t.LastSentAtUtc END
    WHEN NOT MATCHED THEN
        INSERT (PerId, PayloadSnapshot, LastSentAtUtc, LastStatus, LastAttemptAtUtc)
        VALUES (s.PerId,
                CASE WHEN s.ConfirmedByReceiver = 1 THEN s.PayloadSnapshot END,
                CASE WHEN s.ConfirmedByReceiver = 1 THEN SYSUTCDATETIME() END,
                s.Status, SYSUTCDATETIME());

    -- 3) Run item status + counters.
    MERGE dbo.SyncItems AS target
    USING @Items AS source
        ON target.RunId = @RunId AND target.PerId = source.PerId
    WHEN MATCHED THEN
        UPDATE SET
            target.PersonName      = source.PersonName,
            target.Status          = source.Status,
            target.Reason          = source.Reason,
            target.ChangedFields   = source.ChangedFields,
            target.PayloadSnapshot = source.PayloadSnapshot,
            target.AttemptCount    = target.AttemptCount + 1,
            target.UpdatedAtUtc    = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (RunId, PerId, PersonName, Status, Reason, ChangedFields, PayloadSnapshot, AttemptCount, UpdatedAtUtc)
        VALUES (@RunId, source.PerId, source.PersonName, source.Status, source.Reason, source.ChangedFields, source.PayloadSnapshot, 1, SYSUTCDATETIME());

    UPDATE r
    SET LastHeartbeatUtc = SYSUTCDATETIME(),
        ProcessedCount = c.Processed, SentCount = c.Sent, DuplicateCount = c.Duplicate, FailedCount = c.Failed
    FROM dbo.SyncRuns r
    CROSS APPLY (
        SELECT SUM(CASE WHEN Status <> 'Pending' THEN 1 ELSE 0 END) AS Processed,
               SUM(CASE WHEN Status = 'Sent' THEN 1 ELSE 0 END) AS Sent,
               SUM(CASE WHEN Status = 'Duplicate' THEN 1 ELSE 0 END) AS Duplicate,
               SUM(CASE WHEN Status IN ('ValidationFailed', 'SendFailed') THEN 1 ELSE 0 END) AS Failed
        FROM dbo.SyncItems WHERE RunId = @RunId) c
    WHERE r.RunId = @RunId;

    COMMIT TRANSACTION;
END
GO
