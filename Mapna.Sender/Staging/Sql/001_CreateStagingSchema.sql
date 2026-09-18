IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncRuns' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.SyncRuns
    (
        RunId               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SyncRuns PRIMARY KEY,
        StartedAtUtc        DATETIME2(3)      NOT NULL,
        CompletedAtUtc      DATETIME2(3)      NULL,
        LastHeartbeatUtc    DATETIME2(3)      NOT NULL,
        Status              NVARCHAR(30)      NOT NULL,
        TotalCount          INT               NOT NULL,
        ProcessedCount      INT               NOT NULL CONSTRAINT DF_SyncRuns_Processed DEFAULT (0),
        SentCount           INT               NOT NULL CONSTRAINT DF_SyncRuns_Sent DEFAULT (0),
        DuplicateCount      INT               NOT NULL CONSTRAINT DF_SyncRuns_Duplicate DEFAULT (0),
        FailedCount         INT               NOT NULL CONSTRAINT DF_SyncRuns_Failed DEFAULT (0),
        MachineName         NVARCHAR(100)     NULL,
        StopReason          NVARCHAR(500)     NULL
    );
    CREATE INDEX IX_SyncRuns_Status ON dbo.SyncRuns (Status);
    CREATE INDEX IX_SyncRuns_StartedAtUtc ON dbo.SyncRuns (StartedAtUtc DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncItems' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.SyncItems
    (
        RunId               UNIQUEIDENTIFIER NOT NULL,
        PerId               INT              NOT NULL,
        PersonName          NVARCHAR(200)    NULL,
        Status              NVARCHAR(30)     NOT NULL,
        Reason              NVARCHAR(1000)   NULL,
        ChangedFields       NVARCHAR(1000)   NULL,
        PayloadSnapshot     NVARCHAR(MAX)    NULL,
        AttemptCount        INT              NOT NULL CONSTRAINT DF_SyncItems_Attempt DEFAULT (0),
        UpdatedAtUtc        DATETIME2(3)     NOT NULL,
        CONSTRAINT PK_SyncItems PRIMARY KEY (RunId, PerId),
        CONSTRAINT FK_SyncItems_SyncRuns FOREIGN KEY (RunId) REFERENCES dbo.SyncRuns (RunId)
    );
    CREATE INDEX IX_SyncItems_RunId_Status ON dbo.SyncItems (RunId, Status);
END
GO

IF TYPE_ID(N'dbo.SyncItemTableType') IS NULL
BEGIN
    CREATE TYPE dbo.SyncItemTableType AS TABLE
    (
        PerId               INT              NOT NULL,
        PersonName          NVARCHAR(200)    NULL,
        Status              NVARCHAR(30)     NOT NULL,
        Reason              NVARCHAR(1000)   NULL,
        ChangedFields       NVARCHAR(1000)   NULL,
        PayloadSnapshot     NVARCHAR(MAX)    NULL,
        PRIMARY KEY (PerId)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_FlushSyncItemResults
    @RunId UNIQUEIDENTIFIER,
    @Items dbo.SyncItemTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

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

    UPDATE dbo.SyncRuns
    SET
        LastHeartbeatUtc = SYSUTCDATETIME(),
        ProcessedCount = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status <> 'Pending'),
        SentCount      = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status = 'Sent'),
        DuplicateCount = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status = 'Duplicate'),
        FailedCount    = (SELECT COUNT(*) FROM dbo.SyncItems WHERE RunId = @RunId AND Status IN ('ValidationFailed', 'SendFailed'))
    WHERE RunId = @RunId;

    COMMIT TRANSACTION;
END
GO