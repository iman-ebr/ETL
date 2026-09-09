using Mapna.LogData;
using System.Diagnostics;

namespace Mapna.Sender
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource? _cts;
        private AppSettings? _settings;
        private readonly Stopwatch _stopwatch = new();
        private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 500 };

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
                    $"Failed to load configuration file:\n{ex.Message}",
                    "Configuration Error",
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
            lblProgressPercent.Text = "Preparing...";
            lblCurrentStatus.Text = "Running";
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

                lblProgressPercent.Text = "Completed successfully";
                lblCurrentStatus.Text = "Completed";
            }
            catch (OperationCanceledException)
            {
                lblProgressPercent.Text = "Cancelled by user";
                lblCurrentStatus.Text = "Cancelled";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"An unexpected error occurred while syncing:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                lblProgressPercent.Text = "Stopped due to an error";
                lblCurrentStatus.Text = "Error";
            }
            finally
            {
                _stopwatch.Stop();
                _elapsedTimer.Stop();
                UpdateElapsedLabel();
                SetControlsRunningState(isRunning: false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            btnCancel.Enabled = false;
            lblProgressPercent.Text = "Cancelling...";
            lblCurrentStatus.Text = "Cancelling";
        }

        private void UpdateUi(SyncProgress progress)
        {
            var percent = progress.Total == 0
                ? 0
                : (int)((double)progress.Processed / progress.Total * 100);

            progressBar.Value = Math.Min(percent, 100);
            lblProgressPercent.Text = $"{percent}%  —  {progress.Processed} of {progress.Total}"
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
            lblElapsed.Text = $"  |  Elapsed: {_stopwatch.Elapsed:hh\\:mm\\:ss}";
        }

        private void UpdateThroughputLabel(int processed)
        {
            var seconds = _stopwatch.Elapsed.TotalSeconds;
            var rate = seconds > 0.5 ? processed / seconds : 0;
            lblThroughput.Text = $"  |  {rate:0.#} records/sec";
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
        }

        private static string GetStatusText(SyncProgress progress)
        {
            return progress.LastStatus switch
            {
                SendStatus.Sent => "Sent",
                SendStatus.Duplicate => "Duplicate — no change",
                SendStatus.ValidationFailed => "Invalid",
                SendStatus.SendFailed => "Send failed",
                _ => "Unknown"
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