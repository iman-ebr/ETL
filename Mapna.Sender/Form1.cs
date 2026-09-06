using Mapna.Sender;
using Mapna.LogData;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Mapna.Sender
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource? _cts;
        private AppSettings? _settings;

        public Form1()
        {
            InitializeComponent();
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
                    $"خطا در بارگذاری فایل تنظیمات:\n{ex.Message}",
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
            progressBar.Value = 0;
            lblProgressPercent.Text = "در حال آماده‌سازی...";
            SetControlsRunningState(isRunning: true);

            _cts = new CancellationTokenSource();

            var progress = new Progress<SyncProgress>(UpdateUi);

            try
            {
                var orchestrator = new SyncOrchestrator(_settings);
                await orchestrator.RunAsync(progress, _cts.Token);

                lblProgressPercent.Text = "عملیات با موفقیت به پایان رسید";
            }
            catch (OperationCanceledException)
            {
                lblProgressPercent.Text = "عملیات توسط کاربر لغو شد";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"خطای غیرمنتظره در حین همگام‌سازی:\n{ex.Message}",
                    "خطا",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                lblProgressPercent.Text = "عملیات با خطا متوقف شد";
            }
            finally
            {
                SetControlsRunningState(isRunning: false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            btnCancel.Enabled = false;
            lblProgressPercent.Text = "در حال لغو عملیات...";
        }

        private void UpdateUi(SyncProgress progress)
        {
            var percent = progress.Total == 0
                ? 0
                : (int)((double)progress.Processed / progress.Total * 100);

            progressBar.Value = Math.Min(percent, 100);
            lblProgressPercent.Text = $"{percent}% — {progress.Processed} از {progress.Total}";

            lblTotal.Text = $"کل رکوردها: {progress.Total}";
            lblSent.Text = $"  |  ارسال‌شده: {progress.SentCount}";
            lblDuplicate.Text = $"  |  تکراری: {progress.DuplicateCount}";
            lblFailed.Text = $"  |  ناموفق: {progress.FailedCount}";

            AddOrUpdateRow(progress);
        }

        private void AddOrUpdateRow(SyncProgress progress)
        {
            var rowIndex = gridResults.Rows.Add();
            var row = gridResults.Rows[rowIndex];

            row.Cells[0].Value = progress.Processed;
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
                SendStatus.Duplicate => "تکراری - بدون تغییر",
                SendStatus.ValidationFailed => "نامعتبر",
                SendStatus.SendFailed => "خطای ارسال",
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
}