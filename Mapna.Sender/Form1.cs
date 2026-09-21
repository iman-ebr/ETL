using Mapna.Contracts;
using Mapna.LogData;
using Mapna.Sender.Staging;
using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace Mapna.Sender;

public partial class Form1 : Form
{
    private CancellationTokenSource? _cts;
    private AppSettings? _settings;
    private readonly Stopwatch _stopwatch = new();
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 500 };

    private readonly SqlStagingRepository _staging;
    private readonly ILogger _logger;

    private static readonly PersonnelValidator Validator = new();
    private List<PersonnelRecord> _explorerAllRecords = [];
    private readonly List<RecordResult> _currentRunResults = [];

    private Guid? _pendingResumeRunId;
    private Guid? _currentRunId;
    private Color _defaultStatusColor;
    private const int MaxLiveGridRows = 500;


    public Form1(SqlStagingRepository staging, ILogger logger)
    {
        _staging = staging;
        _logger = logger;
        InitializeComponent();
        _elapsedTimer.Tick += (_, _) => UpdateElapsedLabel();
        _defaultStatusColor = lblCurrentStatus.ForeColor;
    }

    private async void Form1_Load(object sender, EventArgs e)
    {
        try
        {
            _settings = AppSettings.Load();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load configuration on Form1 load");
            MessageBox.Show(
                $"بارگذاری فایل پیکربندی با خطا مواجه شد:\n{ex.Message}",
                "خطای پیکربندی",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            btnStart.Enabled = false;
            return;
        }

        await OfferResumeIfAvailableAsync();
    }

    private async Task OfferResumeIfAvailableAsync()
    {
        StagingRun? resumable;
        try
        {
            resumable = await _staging.FindResumableRunAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not check for a resumable run on startup (AppDatabase may be unreachable)");
            return;
        }

        if (resumable is null)
            return;

        var age = DateTime.UtcNow - resumable.LastHeartbeatUtc;
        var message =
            $"یک اجرای ناتمام پیدا شد که در تاریخ {resumable.StartedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm} شروع شده " +
            $"و آخرین فعالیتش {FormatAge(age)} پیش بوده است.\n\n" +
            $"از {resumable.TotalCount} رکورد، {resumable.ProcessedCount} رکورد قبلاً پردازش شده " +
            $"(ارسال: {resumable.SentCount}، تکراری: {resumable.DuplicateCount}، ناموفق: {resumable.FailedCount}).\n\n" +
            "آیا می‌خواهید این اجرا را ادامه دهید؟ رکوردهای باقی‌مانده مجدداً با پایگاه‌داده مبدأ " +
            "بررسی و در صورت نیاز ارسال خواهند شد؛ رکوردهای قبلاً تمام‌شده دوباره پردازش نخواهند شد.\n\n" +
            "در صورت انتخاب «خیر»، یک اجرای کاملاً جدید آغاز خواهد شد.";

        var choice = MessageBox.Show(message, "ادامه‌ی اجرای ناتمام قبلی",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (choice == DialogResult.Yes)
        {
            _pendingResumeRunId = resumable.RunId;
            _currentRunId = resumable.RunId;

            var items = await _staging.GetItemsAsync(resumable.RunId, null, CancellationToken.None);
            _currentRunResults.Clear();
            _currentRunResults.AddRange(items.Select(RecordResult.FromStagingItem));

            lblValueTotal.Text = resumable.TotalCount.ToString();
            lblValueSent.Text = resumable.SentCount.ToString();
            lblValueDuplicate.Text = resumable.DuplicateCount.ToString();
            lblValueFailed.Text = resumable.FailedCount.ToString();

            lblProgressPercent.Text = "آماده برای ادامه — روی «شروع» بزنید";
            lblCurrentStatus.Text = "قابل ادامه";
            lblCurrentStatus.ForeColor = Color.FromArgb(202, 138, 4);

            _logger.Information("User chose to resume run {RunId}", resumable.RunId);
        }
        else
        {
            try
            {
                await _staging.CompleteRunAsync(resumable.RunId, RunStatus.Cancelled, "کاربر تصمیم به عدم ادامه گرفت", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not mark declined run {RunId} as cancelled", resumable.RunId);
            }
            _logger.Information("User declined to resume run {RunId}; marked as abandoned", resumable.RunId);
        }
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1) return "کمتر از یک دقیقه";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} دقیقه";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} ساعت";
        return $"{(int)age.TotalDays} روز";
    }

    private async void btnStart_Click(object sender, EventArgs e)
    {
        if (_settings is null)
            return;

        var resumeRunId = _pendingResumeRunId;
        _pendingResumeRunId = null;

        gridResults.Rows.Clear();
        if (resumeRunId is null)
        {
            _currentRunResults.Clear();
            ResetStatCards();
        }

        progressBar.Value = 0;
        lblProgressPercent.Text = resumeRunId is null ? "در حال آماده‌سازی..." : "در حال ادامه‌ی اجرای قبلی...";
        lblCurrentStatus.Text = "در حال اجرا";
        lblCurrentStatus.ForeColor = _defaultStatusColor;
        SetControlsRunningState(isRunning: true);

        _cts = new CancellationTokenSource();
        _stopwatch.Restart();
        _elapsedTimer.Start();

        var progress = new Progress<SyncProgress>(UpdateUi);

        try
        {
            var orchestrator = new SyncOrchestrator(_settings, _staging, _logger);
            await orchestrator.RunAsync(progress, _cts.Token, resumeRunId);

            lblProgressPercent.Text = "عملیات با موفقیت تکمیل شد";
            lblCurrentStatus.Text = "تکمیل شد";
            lblCurrentStatus.ForeColor = Color.FromArgb(22, 163, 74);
        }
        catch (OperationCanceledException)
        {
            lblProgressPercent.Text = "عملیات توسط کاربر لغو شد — با «شروع» می‌توانید بعداً ادامه دهید";
            lblCurrentStatus.Text = "لغو شد";
            lblCurrentStatus.ForeColor = Color.FromArgb(202, 138, 4);
        }
        catch (ConcurrentRunDetectedException ex)
        {
            MessageBox.Show(ex.Message, "اجرای هم‌زمان", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            lblProgressPercent.Text = "اجرا انجام نشد — یک اجرای دیگر در حال انجام است";
            lblCurrentStatus.Text = "متوقف";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unhandled error surfaced to the UI from the sync run");
            MessageBox.Show(
                $"هنگام همگام‌سازی خطای غیرمنتظره‌ای رخ داد:\n{ex.Message}\n\n" +
                "پیشرفت تا این لحظه در پایگاه‌داده مرکزی ذخیره شده و با اجرای مجدد برنامه، " +
                "امکان ادامه از همین‌جا وجود دارد.",
                "خطا",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            lblProgressPercent.Text = "به دلیل بروز خطا متوقف شد — قابل ادامه در اجرای بعدی";
            lblCurrentStatus.Text = "خطا";
            lblCurrentStatus.ForeColor = Color.FromArgb(220, 38, 38);
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer.Stop();
            UpdateElapsedLabel();
            SetControlsRunningState(isRunning: false);
            _cts?.Dispose();
            _cts = null;

            if (_currentRunId is { } runId)
            {
                try
                {
                    var items = await _staging.GetItemsAsync(runId, null, CancellationToken.None);
                    _currentRunResults.Clear();
                    _currentRunResults.AddRange(items.Select(RecordResult.FromStagingItem));
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not refresh drill-down data for run {RunId} after completion", runId);
                }
            }

            _ = LoadExplorerDataAsync();
        }
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        _cts?.Cancel();
        btnCancel.Enabled = false;
        lblProgressPercent.Text = "در حال لغو...";
        lblCurrentStatus.Text = "در حال لغو";
    }

    private async void btnRefreshExplorer_Click(object? sender, EventArgs e)
    {
        await LoadExplorerDataAsync();
    }

    private async Task LoadExplorerDataAsync()
    {
        if (_settings is null)
            return;

        btnRefreshExplorer.Enabled = false;
        lblExplorerCount.Text = "در حال بارگذاری...";

        try
        {
            var records = await new SourceRepository(_settings.SourceConnectionString).GetAllPersonnelAsync();

            _explorerAllRecords = records.ToList();
            ApplyExplorerFilter(txtSearch.Text);
        }
        catch (Exception ex)
        {
            lblExplorerCount.Text = "خطا در پردازش داده‌ها";
            MessageBox.Show(
                $"هنگام خواندن داده‌ها از پایگاه داده مبدأ خطایی رخ داد:\n{ex.Message}",
                "کاوش داده‌ها",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            btnRefreshExplorer.Enabled = true;
        }
    }

    private void txtSearch_TextChanged(object? sender, EventArgs e)
    {
        ApplyExplorerFilter(txtSearch.Text);
    }

    private void ApplyExplorerFilter(string term)
    {
        IEnumerable<PersonnelRecord> filtered = _explorerAllRecords;

        if (!string.IsNullOrWhiteSpace(term))
        {
            filtered = _explorerAllRecords.Where(r =>
                (r.PerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.PerSurname?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.NationalCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = filtered.ToList();
        gridExplorer.DataSource = new BindingList<PersonnelRecord>(list);

        var invalidCount = list.Count(r => !Validator.Validate(r).IsValid);
        lblExplorerCount.Text = $"{list.Count} رکورد نمایش داده شد  —  {invalidCount} رکورد نامعتبر";
    }

    private void gridExplorer_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (gridExplorer.Rows[e.RowIndex].DataBoundItem is not PersonnelRecord record) return;

        if (!Validator.Validate(record).IsValid)
        {
            e.CellStyle!.BackColor = Color.FromArgb(255, 235, 238);
        }
    }

    private void gridExplorer_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (gridExplorer.Rows[e.RowIndex].DataBoundItem is not PersonnelRecord record) return;

        using var detailForm = new PersonDetailForm(record, Validator);
        detailForm.ShowDialog(this);
    }

    private void gridExplorer_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (gridExplorer.Rows[e.RowIndex].DataBoundItem is not PersonnelRecord record) return;

        var result = Validator.Validate(record);
        e.ToolTipText = result.IsValid
            ? "این رکورد معتبر است"
            : "دلیل رد شدن:\n" + string.Join("\n", result.Errors.Select(x => "• " + x.ErrorMessage));
    }

    private void ShowResultsDetail(Func<RecordResult, bool>? predicate, string title)
    {
        var filtered = predicate is null
            ? _currentRunResults
            : _currentRunResults.Where(predicate).ToList();

        using var dialog = new ResultDetailForm(title, filtered.ToList());
        dialog.ShowDialog(this);
    }

    private void UpdateUi(SyncProgress progress)
    {
        if (progress.RunId is { } runId)
            _currentRunId = runId;

        var percent = progress.Total == 0
            ? 0
            : (int)((double)progress.Processed / progress.Total * 100);

        lblValueTotal.Text = progress.Total.ToString();
        lblValueSent.Text = progress.SentCount.ToString();
        lblValueDuplicate.Text = progress.DuplicateCount.ToString();
        lblValueFailed.Text = progress.FailedCount.ToString();

        if (progress.IsPaused)
        {
            progressBar.Value = Math.Min(percent, 100);
            lblProgressPercent.Text = progress.PauseMessage ?? "در انتظار اتصال...";
            lblCurrentStatus.Text = "متوقف موقت";
            lblCurrentStatus.ForeColor = Color.FromArgb(220, 38, 38);
            return;
        }

        lblCurrentStatus.Text = "در حال اجرا";
        lblCurrentStatus.ForeColor = _defaultStatusColor;

        progressBar.Value = Math.Min(percent, 100);
        lblProgressPercent.Text = $"{percent}%  —  {progress.Processed} از {progress.Total}"
                                  + (string.IsNullOrEmpty(progress.CurrentPerson) ? "" : $"  ({progress.CurrentPerson})");

        UpdateThroughputLabel(progress.Processed);
        AddOrUpdateRow(progress);
    }

    private void UpdateElapsedLabel()
    {
        lblElapsed.Text = $"  |  زمان سپری‌شده: {_stopwatch.Elapsed:hh\\:mm\\:ss}";
    }

    private void UpdateThroughputLabel(int processed)
    {
        var seconds = _stopwatch.Elapsed.TotalSeconds;
        var rate = seconds > 0.5 ? processed / seconds : 0;
        lblThroughput.Text = $"  |  {rate:0.#} رکورد/ثانیه";
    }

    private void ResetStatCards()
    {
        lblValueTotal.Text = "0";
        lblValueSent.Text = "0";
        lblValueDuplicate.Text = "0";
        lblValueFailed.Text = "0";
    }

    private void AddOrUpdateRow(SyncProgress progress)
    {
        if (gridResults.Rows.Count >= MaxLiveGridRows)
            gridResults.Rows.RemoveAt(0);

        var rowIndex = gridResults.Rows.Add();
        var row = gridResults.Rows[rowIndex];

        row.Cells[0].Value = progress.CurrentPerId;
        row.Cells[1].Value = progress.CurrentPerson;
        row.Cells[2].Value = GetStatusText(progress);
        row.Cells[3].Value = progress.LastReason ?? string.Empty;

        row.DefaultCellStyle.BackColor = GetRowColor(progress);

        gridResults.FirstDisplayedScrollingRowIndex = gridResults.Rows.Count - 1;
    }

    private static string GetStatusText(SyncProgress progress)
    {
        return progress.LastStatus switch
        {
            SendStatus.Sent => "ارسال شد",
            SendStatus.Duplicate => "تکراری — بدون تغییر",
            SendStatus.ValidationFailed => "نامعتبر",
            SendStatus.SendFailed => "ارسال ناموفق",
            _ => "نامشخص"
        };
    }

    private static Color GetRowColor(SyncProgress progress)
    {
        return progress.LastStatus switch
        {
            SendStatus.Sent => Color.FromArgb(232, 245, 233),
            SendStatus.Duplicate => Color.FromArgb(245, 245, 245),
            SendStatus.ValidationFailed => Color.FromArgb(255, 235, 238),
            SendStatus.SendFailed => Color.FromArgb(255, 224, 178),
            _ => Color.White
        };
    }

    private void SetControlsRunningState(bool isRunning)
    {
        btnStart.Enabled = !isRunning;
        btnCancel.Enabled = isRunning;
    }
}