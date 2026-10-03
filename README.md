# Mapna Personnel Sync (ETL)

Synchronizes personnel records from the source SQL Server (`PERSONEL_Sender`) to the destination through an
authenticated HTTP API. The goal is a correct, auditable, non-destructive sync: nothing lost, nothing duplicated,
nothing overwritten with stale data. See [`docs/REVIEW.md`](docs/REVIEW.md) for the full review and what each fix
protects against.

| Project | Role |
|---|---|
| `Mapna.Contracts` | `PersonnelRecord`, the single validation contract (`PersonnelValidator`), `PersonnelNormalizer`, `PersonnelFieldLimits` |
| `Mapna.LogData` | EF Core model + migrations: `Personnel` (destination), `SendLogs` / `ReceiveLogs` (audit), `SendStates` (decision state) |
| `Mapna.Receiver` | ASP.NET Core API: API-key auth, rate limiting, atomic per-key upsert, national-code enforcement |
| `Mapna.Sender.Core` | Sync engine: `SyncOrchestrator`, `RecordSender`, `SendDecisionService`, staging/run lock, resilience |
| `Mapna.Sender` | WPF desktop app (MVVM, WPF-UI Fluent, LiveCharts2, Generic Host + DI) |
| `Mapna.Tests` | 28 unit tests + 8 SQL Server integration tests (real orchestrator against the real Receiver, in-process) |

## Run it

Requires the .NET 10 SDK and SQL Server (LocalDB is fine for a demo).

1. **Apply migrations.** A deliberate deployment step; the apps never migrate on their own:
   ```bash
   dotnet ef database update --project Mapna.LogData --startup-project Mapna.LogData --connection "<AppDatabase connection string>"
   ```
   `HardenPersonnelNationalCode` refuses to run while the destination contains duplicate national codes, and lists them.
   Resolve those first.
2. **Receiver:** set `ConnectionStrings:AppDatabase` and `Security:ApiKeys:SenderService` (user-secrets or
   environment variables, never the committed file), then `dotnet run --project Mapna.Receiver`.
3. **Sender:** set `SourceDatabase:ConnectionString`, `AppDatabase:ConnectionString`, `ReceiverApi:BaseUrl`,
   `ReceiverApi:ApiKey` (same key as the Receiver), then `dotnet run --project Mapna.Sender`.
   Every key can also be supplied as an environment variable, e.g. `ReceiverApi__ApiKey`.

**Demo data** (synthetic people, with deliberate defects so the dashboard has something to show):
```bash
pwsh ./tools/Seed-DemoDatabase.ps1
```

**Tests:**
```bash
dotnet test                                   # unit tests
MAPNA_TEST_SQL="Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True" dotnet test   # + integration
```

## Desktop app (WPF)

**Library choice: WPF-UI 4.3** (Fluent Design by lepoco), not MaterialDesignInXAML.
- It is the Windows 11 look: Mica backdrop, Fluent controls, `NavigationView`, `InfoBar`, `Snackbar`.
- It follows the system light/dark theme natively (`SystemThemeWatcher`).
- It is DI/Generic-Host friendly.

Material Design would look like an Android app on a Windows desktop and would bring a second styling system for no
gain. LiveCharts2 draws the throughput and distribution charts. `Microsoft.Toolkit.Uwp.Notifications` shows native
Windows toasts when the window is in the background.

- **Dashboard.** Start / Pause / Continue / Stop.
  - Pause is cooperative, between records, so no request is ever interrupted. Stop is resumable.
  - Five KPI cards, a live throughput chart, a status donut and a resume banner for unfinished runs.
  - A virtualized results grid with search, a segmented status filter and CSV export (UTF-8 BOM, formula-injection safe).
  - A slide-in detail panel: every field of the payload, changed fields highlighted, the server's answer, and the
    correlation id.
- **Explorer.** The source table with per-record validation before anything is sent.
- **History.** Past runs and their per-record outcomes.
- **Settings.** Theme (System / Light / Dark), connection tests, log folder.
- **Notifications.** A snackbar in the app, plus a Windows toast when a run finishes or the connection is lost.
  `Ui:EnableToasts=false` disables toasts.
- **Language.** The UI is Persian, right-to-left, and dates are shown in the Solar Hijri calendar.

Screenshots are in `docs/screenshots/`.
