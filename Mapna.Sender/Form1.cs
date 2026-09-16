using Mapna.Contracts;
using Mapna.LogData;
using System.ComponentModel;
using System.Diagnostics;

namespace Mapna.Sender;

public partial class Form1 : Form
{
    private CancellationTokenSource? _cts;
    private AppSettings? _settings;
    private readonly Stopwatch _stopwatch = new();
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 500 };

    private static readonly PersonnelValidator Validator = new();
    private List<PersonnelRecord> _explorerAllRecords = [];
    private readonly List<RecordResult> _currentRunResults = [];

    public Form1()
    {
        InitializeComponent();
        _elapsedTimer.Tick += (_, _) => UpdateElapsedLabel();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        try
        {
            _settings = AppSettings.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"بارگذاری فایل پیکربندی با خطا مواجه شد:\n{ex.Message}",
                "خطای پیکربندی",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            btnStart.Enabled = false;
        }
    }

    private async void btnStart_Click(object sender, EventArgs e)
    {
        if (_settings is null)
            return;

        gridResults.Rows.Clear();
        _currentRunResults.Clear();
        progressBar.Value = 0;
        lblProgressPercent.Text = "در حال آماده‌سازی...";
        lblCurrentStatus.Text = "در حال اجرا";
        ResetStatCards();
        SetControlsRunningState(isRunning: true);

        _cts = new CancellationTokenSource();
        _stopwatch.Restart();
        _elapsedTimer.Start();

        var progress = new Progress<SyncProgress>(UpdateUi);

        try
        {
            var orchestrator = new SyncOrchestrator(_settings);
            await orchestrator.RunAsync(progress, _cts.Token);

            lblProgressPercent.Text = "عملیات با موفقیت تکمیل شد";
            lblCurrentStatus.Text = "تکمیل شد";
        }
        catch (OperationCanceledException)
        {
            lblProgressPercent.Text = "عملیات توسط کاربر لغو شد";
            lblCurrentStatus.Text = "لغو شد";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"هنگام همگام‌سازی خطای غیرمنتظره‌ای رخ داد:\n{ex.Message}",
                "خطا",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            lblProgressPercent.Text = "به دلیل بروز خطا متوقف شد";
            lblCurrentStatus.Text = "خطا";
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer.Stop();
            UpdateElapsedLabel();
            SetControlsRunningState(isRunning: false);
            _cts?.Dispose();
            _cts = null;

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
            var records = await Task.Run(() =>
                new SourceRepository(_settings.SourceConnectionString).GetAllPersonnel());

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

        using var dialog = new ResultDetailForm(title, filtered);
        dialog.ShowDialog(this);
    }

    private void UpdateUi(SyncProgress progress)
    {
        var percent = progress.Total == 0
            ? 0
            : (int)((double)progress.Processed / progress.Total * 100);

        progressBar.Value = Math.Min(percent, 100);
        lblProgressPercent.Text = $"{percent}%  —  {progress.Processed} از {progress.Total}"
                                  + (string.IsNullOrEmpty(progress.CurrentPerson) ? "" : $"  ({progress.CurrentPerson})");

        lblValueTotal.Text = progress.Total.ToString();
        lblValueSent.Text = progress.SentCount.ToString();
        lblValueDuplicate.Text = progress.DuplicateCount.ToString();
        lblValueFailed.Text = progress.FailedCount.ToString();

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
        var rowIndex = gridResults.Rows.Add();
        var row = gridResults.Rows[rowIndex];

        row.Cells[0].Value = progress.CurrentPerId;
        row.Cells[1].Value = progress.CurrentPerson;
        row.Cells[2].Value = GetStatusText(progress);
        row.Cells[3].Value = progress.LastReason ?? string.Empty;

        row.DefaultCellStyle.BackColor = GetRowColor(progress);

        gridResults.FirstDisplayedScrollingRowIndex = gridResults.Rows.Count - 1;

        _currentRunResults.Add(new RecordResult
        {
            PerId = progress.CurrentPerId,
            PersonName = progress.CurrentPerson,
            Status = progress.LastStatus,
            Reason = progress.LastReason
        });
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