# Mapna ETL — pre-ship review (data-integrity first)

Scope: every source file in the delivered `ETL.zip` (Contracts, LogData, Receiver, Sender incl. the `Staging/`
subsystem, which the brief did not mention), plus the fixes and the WPF rewrite in this package.

**How the claims below were checked.**
- The original solution was built as zipped.
- Fixes were verified by 28 unit tests and 8 SQL Server integration tests. The integration tests run the real
  orchestrator against the real Receiver in-process, one throw-away database per test.
- The migrations were applied to a production-like database (existing `SendLogs` history, duplicate national codes).
- A 4,201-row demo was run end-to-end through the WPF UI, including a Receiver outage in the middle of a run.
- Where a claim needed proof about the *old* code, the old code path was executed. One suspected bug did not
  reproduce and was dropped (see the end of this document).

Severity: **CRITICAL** = can corrupt, duplicate or silently lose data or decision state, or blocks shipping.
**HIGH** = wrong results or an operational outage. **MEDIUM** = latent risk. **LOW** = hygiene.

---

## CRITICAL

### C1. The zipped solution does not compile
`SyncOrchestrator` still calls `SendDecisionService.LoadLastSentAsync(...)` (renamed to `LoadStatesAsync`) and
`new RecordSender(httpclient, logdb)` (the constructor now needs 3 arguments). The `SendStates` refactor is half-applied.
*Repro:* `dotnet build Mapna.slnx` gives CS0117 and CS7036.
*Fix:* see C3. The new design removes the shared dictionary entirely.

### C2. `SendStates` has no migration, and no backfill
`LogDbContext` maps `SendStates`, but no migration creates it (the model snapshot has no `SendState` entity).
- The first run throws `Invalid object name 'SendStates'`.
- Even with the table created by hand, it starts empty, so the first run after deployment re-sends every record.

*Fix:* `Migrations/20260929131634_AddSendStatesAndAuditCorrelation.cs` creates the table and seeds it from the audit
log (the last `Sent` payload per PerId). It was verified on a DB that has `Sent`, `SendFailed` and `ValidationFailed`
history: only the latest `Sent` payload is picked.

### C3. `ChangeTracker.Clear()` silently discards every state update after the first flush
`LoadStatesAsync` loads all `SendState` rows **tracked**, and `RecordSender.UpsertState` later mutates them.
`FlushWithConnectivityPauseAsync` calls `logdb.ChangeTracker.Clear()` after the first `SaveChanges` (after 50 records
or 2 s), which **detaches every loaded state**. From then on, updates to existing PerIds change detached objects, and
`SaveChanges` never sees them.

*Repro:*
1. Sync.
2. Change 80 people in the source and sync again. The run reports 80 sent, but only the first handful of snapshots
   are persisted.
3. Sync again. Most of the 80 are "changed" again and re-sent, on every run, forever.

This defeats the exact fix you're making.
*Fix:* states are read `AsNoTracking()` and **written only by** `dbo.usp_FlushSyncResults` (a SQL `MERGE`), never
through the change tracker.
*Regression test:* `Changes_are_sent_exactly_once_and_decision_state_survives_across_runs`. It sends 80 changes,
re-runs and asserts `Sent = 0`, then deletes `SendLogs` and asserts `Sent = 0` again.

### C4. Two Sender instances can run at the same time and overwrite newer data with older data
The guard is `FindActiveRunAsync` then `StartRunAsync`: two separate calls (check-then-act). "Active" means a heartbeat
newer than *reader clock − 2 min*, but heartbeats are written with *writer* `DateTime.UtcNow`.
- **Race:** two operators press Start within the same second. Both pass.
- **Clock skew:** machine B runs 3 minutes fast, so it sees A's live run as stale and starts, or offers to *resume* it.
- **Outage:** during an AppDatabase outage or a long EF retry, no heartbeat is written. After 2 minutes another
  instance takes over while the first is still sending.
- **Resume bypass:** `activeElsewhere.RunId != resumeRunId` lets a user resume a run that is *actively running
  elsewhere*.

*Consequence:* instance A (source read at T0) and B (read at T1) both send PerId X. If A's request lands last, the
destination goes back to the T0 values, and `SendStates` says whatever the last writer said.

*Fix:* `Staging/SyncRunLock.cs` uses `sp_getapplock` (Exclusive, Session-owned) on a dedicated **non-pooled**
connection held for the whole run.
- If the process dies, the session ends and the lock is released. No heartbeats, no clocks.
- Resumable runs are found with `APPLOCK_TEST(...) = 1` ("nobody holds the lock"), not with clock arithmetic.
- The lock is re-verified before every flush. If it was lost, the run stops rather than continuing with an in-memory
  snapshot that another instance may already have superseded.

```csharp
await using var runLock = await SyncRunLock.TryAcquireAsync(_settings.AppConnectionString, ct);
if (runLock is null) throw new ConcurrentRunDetectedException(...);
```
*Test:* `A_second_instance_cannot_run_while_the_lock_is_held`.

### C5. The Receiver's read-compare-write is not atomic (lost updates)
`PersonnelUpsertService` reads the row, diffs it and writes it with no transaction and no lock. Two concurrent requests
for the same PerId both read version V0 and both write, so the last one wins, whichever it is. Triggers:
- two senders (C4);
- Polly retrying a request that timed out on the client but is still executing on the server.

The insert race was "handled" by catching the unique violation, but `ChangeTracker.Clear()` there also threw away the
NationalCode warning log.
*Fix:* the Receiver now runs inside an execution-strategy transaction with key-range **U-locks**. U-locks are
incompatible with each other, so same-key requests serialize without deadlocking:
```csharp
var existing = await _db.Personnel
    .FromSqlInterpolated($"SELECT * FROM dbo.Personnel WITH (UPDLOCK, HOLDLOCK) WHERE PerId = {record.PerId}")
    .SingleOrDefaultAsync();
```
A replay after an ambiguous commit re-runs the whole block and answers `Duplicate`, so it is idempotent.

### C6. National-code collisions were accepted, so the same person could be stored twice
`CheckNationalCodeConflictAsync` only **logged a warning**, then inserted anyway. The check is also racy (no lock) and
cannot be indexed (`nvarchar(max)`), so it runs a table scan per request.
*Fix:*
- The Receiver rejects the record with **409** (`RejectedNationalCodeConflict`, including the owning PerId), under a
  U-lock.
- `UX_Personnel_NationalCode` (unique, `nvarchar(10)`) enforces the rule in the database.
- Migration `HardenPersonnelNationalCode` **refuses to run** while duplicates exist and lists them. It never
  truncates and never picks a winner. Verified: it aborted with the duplicate list, then applied once the duplicate
  was resolved.

> This is a business-policy change (reject instead of warn). Get sign-off; a re-hire under a new PerId now needs a
> human decision.

*Test:* `National_code_collision_is_rejected_instead_of_creating_a_second_person`.

### C7. One bad value makes a run pause-and-retry forever
Every policy retried **any** `SqlException` / `DbUpdateException` and fed it into a circuit breaker. On "circuit open"
the orchestrator waited 32 s and retried the same batch, with no limit. Deterministic errors therefore became infinite
loops (the heartbeat also stops, which feeds C4):

| Trigger | Where |
|---|---|
| `Reason` longer than `NVARCHAR(1000)`. `"Api responded with 502: " + body` for an IIS/proxy HTML page, or a long exception message | `SyncItemTableType.Reason` |
| `PersonName` longer than 200 (truncated in `StageBatchAsync` but **not** in `FlushResultsAsync`) | `SyncItemTableType.PersonName` |
| EF `EnableRetryOnFailure` replays an `INSERT SendStates` after an ambiguous commit, giving PK violation 2627 | `SendStates` |
| Two adjacent records the Receiver answers with 500: 4+ failures open the HTTP breaker, and the orchestrator re-sends the same poison record through it forever | HTTP |

The first two were **reproduced against the original stored procedure**: `SqlException 2628: String or binary data
would be truncated … column 'Reason'` (and `'PersonName'`).
*Fix:*
- `SqlTransientErrors.IsTransient` means only real transient errors are retried.
- All text is fitted to its column before the TVP is built.
- The `MERGE` / `CorrelationId` writes are idempotent.
- The HTTP breaker opens only on *connectivity* failures (network, timeout, 502/503/504).
- A reachable-but-failing record gets 4 bounded rounds and is then recorded as `SendFailed` (it will be re-sent next
  run, because its snapshot did not advance).

### C8. Any HTTP timeout aborted the whole run and labelled it "cancelled by user"
`HttpClient.Timeout` throws `TaskCanceledException`, which is an `OperationCanceledException`. `RecordSender` and the
orchestrator rethrew every OCE, so one slow request (for example a lock wait on the Receiver) ended the run with
`wasCancelled = true`. The UI then said "you can resume with Start", but `Cancelled` runs are **not** returned by
`FindResumableRunAsync`.
*Fix:* `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`; every other OCE is a
`TransientSendException`. A user stop is stored as `Paused`, which is resumable.
*Test:* `A_timeout_is_transient_not_a_user_cancellation`.

---

## HIGH

### H1. The audit log, decision state and run status were written by two separate transactions
EF `SaveChanges` wrote `SendLogs` + `SendStates`; a separate Dapper call then wrote `SyncItems`. Worse, the `finally`
block **still flushed staging after `SendLogs` failed to save**. Its log line says "left Pending so they self-heal",
but the code does the opposite. The result is records marked Sent with no audit row.
*Fix:* `dbo.usp_FlushSyncResults` writes all three (plus counters) in **one** transaction. It is idempotent on
`SendLogs.CorrelationId` (unique filtered index), so replaying a batch never duplicates audit rows (test
`Replaying_a_flush_after_an_ambiguous_commit_does_not_duplicate_audit_rows`).

### H2. The retry policy mishandled 429 and configuration errors
- 429 was retried after 2/4/8 s, but the limiter asks for `Retry-After: 60`. After 14 s the 429 fell through and the
  record was stored as **final** `SendFailed`, which resume skips.
- 401/403/404 marked **every** record failed (a wrong key produced 4,000 "failures") instead of stopping.

*Fix:* Polly v8 via `Microsoft.Extensions.Http.Resilience` (`ReceiverHttpPipeline.cs`) with `ShouldRetryAfterHeader`,
a per-attempt timeout and a total timeout. `RecordSender` classifies explicitly:

| Result | Handling |
|---|---|
| 2xx | Sent (confirmed) |
| 400 / 409 / 422 | fail this record |
| 401 / 403 / 404 / 405 | `ReceiverConfigurationException`: stop the run, resumable |
| 408 / 429 / 5xx / network | transient; the record stays Pending |

### H3. Unicode digits
.NET `\d` matches Persian digits, so `^09\d{9}$` accepted **mixed** input such as `09۱۲۳۴۵۶۷۸۹` (ASCII prefix,
Persian keyboard). That value was then sent as-is to the destination. `char.IsDigit` also accepts Persian digits,
which broke the `c - '0'` arithmetic of the national-code check, so real people with Persian-digit codes were rejected.
*Fix:*
- `PersonnelNormalizer` converts digits to ASCII and trims values at the source edge. Arabic ي/ك → Persian ی/ک is
  opt-in via `Sync:NormalizeArabicLetters`.
- The validator only accepts `[0-9]`.

> Normalization changes canonical values, so affected rows are sent **once** after deployment (visible as
> ChangedFields in the audit log).

### H4. One duplicated `PER_ID` in the source blocked the whole sync
`records.ToDictionary(r => r.PerId)` throws on the first duplicate. *Fix:* `SourceSnapshot` quarantines duplicated
PerIds as `ValidationFailed` and **never sends either row**, because guessing would risk overwriting a real person with
the wrong row. Test: `Duplicate_perId_in_source_is_quarantined_and_does_not_block_the_rest`.

### H5. The UI showed records as "Sent" before they were sent
`SyncProgress.LastStatus` was a non-nullable enum that defaulted to `Sent`. Every "paused"/"connection restored" report
added a grid row saying the current record was **Sent**. *Fix:* it is now `SendStatus?`, with `IsRecordResult`.

### H6. A bad `BaseUrl` left an orphan "Running" run
`new Uri("")` threw after staging and **outside** the try/finally, which blocked the next start. Also, a base URL
without a trailing slash silently drops its path (`https://host/receiver` + `api/personnel` resolves to
`https://host/api/personnel`). *Fix:* `AppSettings.Validate()` runs before any run is created, and `ReceiverBaseUri`
normalizes the slash.

---

## MEDIUM

- **M1. Resume semantics.**
  - Records added to the source after a run started were silently skipped on resume.
  - Records deleted from the source stayed `Pending` forever.
  - A resumed run's final status used only this session's counters.

  *Fix:* staging is idempotent, so resume stages newcomers. Vanished records get an explicit outcome. `CompleteRunAsync`
  derives `Completed` / `CompletedWithFailures` and the counters from `SyncItems`.
- **M2. Migrations.**
  - EF scaffolded `int → bigint` on the PK as a bare `ALTER COLUMN`, which **SQL Server rejects** ("PK_SendLogs is
    dependent on column Id"). The integration tests caught it; the PK is now dropped and re-added around the change.
  - `Down()` would drop `SendStates` (a full resend), so it now refuses.
  - Enums are stored as strings: **never rename** a member.
  - TVPs cannot be `ALTER`ed, and `IF TYPE_ID IS NULL CREATE` silently keeps an old shape. A new shape needs a new
    name (`SyncResultTableType`).
  - Both apps now refuse to run against a schema with pending migrations.
- **M3. Audit volume.**
  - `SendLogs.Id` / `ReceiveLogs.Id` were `int`, and **every unchanged record is logged every run**. With 10k people
    every 5 minutes that's about 2.9M rows/day, so `int` runs out in about 2 years. That volume is also why a cleanup
    job exists at all.
  - The cleanup deletes by date but there was no `OccurredAtUtc` index.

  *Fix:* `bigint`, the index, and the cleanup no longer affects decisions. Consider not logging `Duplicate` per record,
  since `SyncItems` already has it per run.
- **M4. Nested retries.** EF `EnableRetryOnFailure` (5 retries, up to 10 s) × Polly retry (3) × breaker gave up to 24
  attempts per flush and hid outages from the UI for minutes. The Sender no longer writes through EF.
- **M5. Cross-system traceability.** `SendLogs` (sender clock) and `ReceiveLogs` (server clock) could only be
  correlated by time. *Fix:* an `X-Correlation-Id` header is stored on both sides; the test joins them 1:1.
- **M6. Validation gaps.**
  - There were no length limits (now `PersonnelFieldLimits`; **confirm the numbers against the real destination
    schema**).
  - `BornDate` is free text: decide the format, then add a rule. I did not guess one, because a wrong guess would
    reject everyone.
  - `SexCode` and `PerStatus` are unconstrained.
  - SQL `NULL` in non-nullable fields becomes `""` in the destination.
- **M7. Source read.**
  - The default 30 s timeout on a full-table read, with 3 retries, meant 4 full scans before giving up (now 180 s).
  - Under `READ COMMITTED` the read isn't a point-in-time snapshot. Use `SNAPSHOT` isolation if the DBA enables it.
- **M8. Semantics to confirm with the business (not bugs).**
  - Deletions in the source are never propagated.
  - Direct edits in the destination are never detected. Decisions compare only against the Sender's own last
    confirmed payload.
  - If the source has a `rowversion`, send it and add a guard in the Receiver:
    `UPDATE … WHERE PerId = @id AND SourceVersion < @incoming`. That makes out-of-order delivery harmless even outside
    this Sender.

## LOW

- Both `Mapna.Receiver/Auth/*.cs` file names started with an invisible U+202B (right-to-left embedding) character.
  Renamed.
- A real-looking API key and connection strings are committed in `appsettings.json`, and `TrustServerCertificate=True`
  is set. The Sender now refuses `CHANGE-ME*` keys; use user-secrets or environment variables in production.
- PII (national code, mobile, address) is duplicated in `SendLogs`, `SendStates` and `SyncItems`, and `SyncRuns` /
  `SyncItems` have no retention policy.
- `HttpResponseMessage` was not disposed.
- `PlayLoadHash` is misspelled and never written. Don't "fix" it by renaming the property (EF would DROP + ADD it);
  use `RenameColumn`.
- WinForms ran the orchestrator's CPU work (JSON, reflection diffing) on the UI thread.

## What the idempotency guarantee now is (precisely)

Delivery is **at-least-once**. The destination write is **effectively-once** because:
1. there is a single writer (C4);
2. the Receiver upsert is atomic, per-key serialized and content-idempotent (C5);
3. a record's state advances only on a 2xx, in the same transaction as its audit row (H1).

The one remaining ambiguity is a request whose response is lost (a user stop, or a crash mid-request). The record stays
Pending, is re-sent on resume, and the Receiver answers `Duplicate`. Test
`Stopped_run_is_resumable_and_every_record_is_applied_exactly_once` asserts **60 people inserted exactly once** with
0–1 duplicate answers. The end-to-end outage demo sent 4,183 updates through a Receiver restart with **0** duplicate
answers.

## Retracted during verification

- *"ASP.NET's implicit `[Required]` rejects empty `PerLName`."* Disproved: with the fix reverted, an empty Latin name
  still gets through. `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` is kept only so that
  `PersonnelValidator` stays the single contract; it is not a bug fix.
- *"A fully Persian-digit mobile passed validation."* Imprecise: the literal `09` prefix already rejected it. The real
  hole is **mixed** digits (H3).
