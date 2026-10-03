using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Mapna.LogData;
using Mapna.Sender.Services;
using Mapna.Sender.Staging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SkiaSharp;
using Wpf.Ui.Controls;

namespace Mapna.Sender.ViewModels;

public enum RunState
{
    Idle,
    Preparing,
    Running,
    Pausing,
    Paused,
    WaitingForConnection,
    Stopping,
    Completed,
    CompletedWithFailures,
    Stopped,
    Failed
}

public partial class DashboardViewModel : ObservableObject
{
    public static readonly SKColor SentColor = SKColor.Parse("#10B981");
    public static readonly SKColor DuplicateColor = SKColor.Parse("#94A3B8");
    public static readonly SKColor FailedColor = SKColor.Parse("#EF4444");
    public static readonly SKColor ThroughputColor = SKColor.Parse("#3B82F6");
    private const int MaxThroughputPoints = 180;

    private readonly IServiceProvider _services;
    private readonly SqlStagingRepository _staging;
    private readonly ConnectivityProbe _probe;
    private readonly INotificationService _notifications;
    private readonly IFileExportService _export;
    private readonly AppThemeService _theme;
    private readonly ILogger _logger;

    private readonly ConcurrentQueue<SyncProgress> _inbox = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _searchDebounce;
    private readonly Stopwatch _runClock = new();
    private readonly Queue<(double Seconds, int Processed)> _rateWindow = new();
    private CancellationTokenSource? _cts;
    private PauseTokenSource? _pause;
    private SyncPauseKind _lastPauseKind;
    private double _lastSampleSecond = -1;
    private int _sessionStartProcessed = -1;
    private int _sequence;
    private bool _initialized;

    private readonly ObservableValue _sentValue = new(0);
    private readonly ObservableValue _duplicateValue = new(0);
    private readonly ObservableValue _failedValue = new(0);

    public DashboardViewModel(IServiceProvider services, SqlStagingRepository staging, ConnectivityProbe probe,
        INotificationService notifications, IFileExportService export, AppThemeService theme, ILogger logger)
    {
        _services = services;
        _staging = staging;
        _probe = probe;
        _notifications = notifications;
        _export = export;
        _theme = theme;
        _logger = logger;

        ResultsView = CollectionViewSource.GetDefaultView(Results);
        ResultsView.Filter = o => o is RecordResultItem item && IsVisible(item);

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _uiTimer.Tick += (_, _) => DrainInbox();

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RefreshFilter();
        };

        StatusSeries =
        [
            new PieSeries<ObservableValue> { Name = "Sent", Values = [_sentValue], Fill = new SolidColorPaint(SentColor), InnerRadius = 62, HoverPushout = 4 },
            new PieSeries<ObservableValue> { Name = "Duplicate", Values = [_duplicateValue], Fill = new SolidColorPaint(DuplicateColor), InnerRadius = 62, HoverPushout = 4 },
            new PieSeries<ObservableValue> { Name = "Failed", Values = [_failedValue], Fill = new SolidColorPaint(FailedColor), InnerRadius = 62, HoverPushout = 4 }
        ];

        ThroughputSeries =
        [
            new LineSeries<ObservablePoint>
            {
                Name = "rec/s",
                Values = ThroughputPoints,
                GeometrySize = 0,
                LineSmoothness = 0.65,
                Stroke = new SolidColorPaint(ThroughputColor, 2.5f),
                Fill = new LinearGradientPaint(
                    [ThroughputColor.WithAlpha(110), ThroughputColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0), new SKPoint(0.5f, 1))
            }
        ];

        XAxes = [new Axis { TextSize = 11, MinLimit = 0, Labeler = v => Display.Duration(TimeSpan.FromSeconds(Math.Max(0, v))), MinStep = 1 }];
        YAxes = [new Axis { TextSize = 11, MinLimit = 0, MinStep = 1, Labeler = v => v.ToString("0") }];
        ApplyChartTheme();
        _theme.ThemeChanged += (_, _) => ApplyChartTheme();
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle), nameof(IsPaused), nameof(IsPreparing), nameof(ShowPauseButton), nameof(ShowContinueButton),
        nameof(StateSeverity), nameof(StateSymbol), nameof(ShowResumeOffer), nameof(ShowEnvironmentRetry))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(PauseCommand), nameof(ContinueCommand), nameof(StopCommand), nameof(ResumePreviousCommand), nameof(DiscardPreviousCommand))]
    public partial RunState State { get; set; } = RunState.Idle;

    [ObservableProperty] public partial string StateText { get; set; } = "آماده";
    [ObservableProperty] public partial string PhaseText { get; set; } = "برای شروع همگام‌سازی، «شروع» را بزنید.";
    [ObservableProperty] public partial Guid? CurrentRunId { get; set; }

    public bool IsBusy => State is RunState.Preparing or RunState.Running or RunState.Pausing or RunState.Paused or RunState.WaitingForConnection or RunState.Stopping;
    public bool IsIdle => !IsBusy;
    public bool IsPaused => State == RunState.Paused;
    public bool IsPreparing => State == RunState.Preparing;
    public bool ShowResumeOffer => IsIdle && ResumableRun is not null;
    public bool ShowEnvironmentRetry => IsIdle && !IsEnvironmentReady && !IsCheckingEnvironment;
    public bool ShowPauseButton => IsBusy && State != RunState.Paused;
    public bool ShowContinueButton => State == RunState.Paused;

    public InfoBarSeverity StateSeverity => State switch
    {
        RunState.Completed => InfoBarSeverity.Success,
        RunState.CompletedWithFailures or RunState.Paused or RunState.Pausing or RunState.WaitingForConnection or RunState.Stopped => InfoBarSeverity.Warning,
        RunState.Failed => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational
    };

    public SymbolRegular StateSymbol => State switch
    {
        RunState.Running or RunState.Preparing => SymbolRegular.ArrowSync24,
        RunState.Paused or RunState.Pausing => SymbolRegular.Pause24,
        RunState.WaitingForConnection => SymbolRegular.PlugDisconnected24,
        RunState.Completed => SymbolRegular.CheckmarkCircle24,
        RunState.CompletedWithFailures => SymbolRegular.Warning24,
        RunState.Failed => SymbolRegular.ErrorCircle24,
        RunState.Stopping or RunState.Stopped => SymbolRegular.Stop24,
        _ => SymbolRegular.CircleSmall24
    };


    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasData))] public partial int Total { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasData))] public partial int Processed { get; set; }
    [ObservableProperty] public partial int SentCount { get; set; }
    [ObservableProperty] public partial int DuplicateCount { get; set; }
    [ObservableProperty] public partial int FailedCount { get; set; }
    [ObservableProperty] public partial double ProgressPercent { get; set; }
    [ObservableProperty] public partial string CurrentPerson { get; set; } = string.Empty;
    [ObservableProperty] public partial string ElapsedText { get; set; } = "00:00";
    [ObservableProperty] public partial string EtaText { get; set; } = "—";
    [ObservableProperty] public partial double Throughput { get; set; }

    public bool HasData => Processed > 0;


    [ObservableProperty] public partial bool IsBannerOpen { get; set; }
    [ObservableProperty] public partial string BannerTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string BannerMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial InfoBarSeverity BannerSeverity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnvironmentRetry))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(ResumePreviousCommand))]
    public partial bool IsEnvironmentReady { get; set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowEnvironmentRetry))] public partial bool IsCheckingEnvironment { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResumableRun), nameof(ResumableSummary), nameof(ShowResumeOffer))]
    [NotifyCanExecuteChangedFor(nameof(ResumePreviousCommand), nameof(DiscardPreviousCommand))]
    public partial StagingRun? ResumableRun { get; set; }

    public bool HasResumableRun => ResumableRun is not null;

    public string ResumableSummary => ResumableRun is not { } r ? string.Empty :
        $"آغاز در {Display.Date(r.StartedAtUtc)} روی {r.MachineName} — {r.ProcessedCount:N0} از {r.TotalCount:N0} رکورد پردازش شده " +
        $"(ارسال {r.SentCount:N0}، بدون تغییر {r.DuplicateCount:N0}، ناموفق {r.FailedCount:N0}). " +
        "رکوردهای باقی‌مانده دوباره با مبدأ بررسی می‌شوند؛ رکوردهای تمام‌شده دوباره ارسال نمی‌شوند.";


    public ObservableCollection<RecordResultItem> Results { get; } = [];
    public ICollectionView ResultsView { get; }

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial ResultFilter ActiveFilter { get; set; } = ResultFilter.All;
    [ObservableProperty] public partial int VisibleCount { get; set; }

    [ObservableProperty] public partial RecordResultItem? SelectedResult { get; set; }
    [ObservableProperty] public partial RecordDetailViewModel? Detail { get; set; }
    [ObservableProperty] public partial bool IsDetailOpen { get; set; }


    public ISeries[] StatusSeries { get; }
    public ObservableCollection<ObservablePoint> ThroughputPoints { get; } = [];
    public ISeries[] ThroughputSeries { get; }
    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await CheckEnvironmentAsync();
    }

    [RelayCommand]
    private async Task CheckEnvironmentAsync()
    {
        IsCheckingEnvironment = true;
        try
        {
            var status = await Task.Run(() => _probe.CheckAppDatabaseAsync());
            IsEnvironmentReady = status.IsReady;
            if (!status.AppDatabaseReachable)
                ShowBanner("اتصال به پایگاه‌داده برقرار نیست", $"پایگاه‌داده برنامه در دسترس نیست. تنظیمات اتصال را بررسی و دوباره تلاش کنید.\n{status.Error}", InfoBarSeverity.Error);
            else if (status.PendingMigrations.Count > 0)
                ShowBanner("ساختار پایگاه‌داده به‌روز نیست",
                    $"پیش از همگام‌سازی باید Migrationهای زیر اعمال شوند: {string.Join("، ", status.PendingMigrations)}", InfoBarSeverity.Error);
            else if (status.Error is not null)
                ShowBanner("خطا در آماده‌سازی پایگاه‌داده", status.Error, InfoBarSeverity.Error);
            else
                IsBannerOpen = false;

            if (status.IsReady)
                await RefreshResumableAsync();
        }
        finally
        {
            IsCheckingEnvironment = false;
        }
    }

    private async Task RefreshResumableAsync()
    {
        try
        {
            ResumableRun = await Task.Run(() => _staging.FindResumableRunAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not check for a resumable run");
            ResumableRun = null;
        }
    }


    private bool CanStart() => IsIdle && IsEnvironmentReady;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => RunAsync(resumeRunId: null);

    private bool CanResumePrevious() => IsIdle && IsEnvironmentReady && ResumableRun is not null;

    [RelayCommand(CanExecute = nameof(CanResumePrevious))]
    private Task ResumePreviousAsync() => RunAsync(ResumableRun!.RunId);

    private bool CanDiscardPrevious() => IsIdle && ResumableRun is not null;

    [RelayCommand(CanExecute = nameof(CanDiscardPrevious))]
    private async Task DiscardPreviousAsync()
    {
        if (ResumableRun is not { } run) return;
        try
        {
            await _staging.CompleteRunAsync(run.RunId, RunStatus.Cancelled, "کاربر تصمیم به عدم ادامه گرفت", CancellationToken.None);
            _logger.Information("User declined to resume run {RunId}; marked as cancelled", run.RunId);
            ResumableRun = null;
        }
        catch (Exception ex)
        {
            _notifications.Show("خطا", $"وضعیت اجرای قبلی تغییر نکرد: {ex.Message}", NotificationKind.Error);
        }
    }

    private bool CanPause() => State is RunState.Running or RunState.WaitingForConnection;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _pause?.Pause();
        State = RunState.Pausing;
        StateText = "در حال توقف موقت…";
        PhaseText = "پس از اتمام رکورد جاری متوقف می‌شود (هیچ درخواستی نیمه‌کاره نمی‌ماند).";
    }

    private bool CanContinue() => State == RunState.Paused;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        _pause?.Resume();
        State = RunState.Running;
        StateText = "در حال اجرا";
    }

    private bool CanStop() => IsBusy && State != RunState.Stopping;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
        State = RunState.Stopping;
        StateText = "در حال توقف…";
        PhaseText = "نتایج تا این لحظه ذخیره می‌شوند؛ اجرا بعداً قابل ادامه است.";
    }

    [RelayCommand]
    private void SetFilter(ResultFilter filter) => ActiveFilter = filter;

    [RelayCommand]
    private void CloseDetail() => SelectedResult = null;

    [RelayCommand]
    private void CopyDetailJson()
    {
        if (Detail?.PrettyJson is { } json)
        {
            Clipboard.SetText(json);
            _notifications.Show("کپی شد", "محتوای رکورد در کلیپ‌بورد قرار گرفت.", NotificationKind.Info);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var rows = Results.Where(IsVisible).OrderBy(r => r.Sequence).ToList();
        if (rows.Count == 0)
        {
            _notifications.Show("خروجی", "رکوردی برای خروجی وجود ندارد.", NotificationKind.Info);
            return;
        }

        var path = await _export.ExportCsvAsync($"sync-{CurrentRunId?.ToString()[..8] ?? "results"}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        [
            ("ردیف", r => r.Sequence),
            ("PerId", r => r.PerId),
            ("نام", r => r.PersonName),
            ("وضعیت", r => r.StatusText),
            ("فیلدهای تغییرکرده", r => r.ChangedFieldsText),
            ("دلیل", r => r.Reason),
            ("CorrelationId", r => r.CorrelationId),
            ("زمان", r => r.TimeText)
        ], rows);

        if (path is not null)
            _notifications.Show("خروجی ذخیره شد", $"{rows.Count:N0} رکورد در {System.IO.Path.GetFileName(path)} ذخیره شد.", NotificationKind.Success);
    }


    private async Task RunAsync(Guid? resumeRunId)
    {
        if (IsBusy) return;

        ResetRun();
        State = RunState.Preparing;
        StateText = resumeRunId is null ? "در حال آماده‌سازی" : "در حال ادامه‌ی اجرای قبلی";
        IsBannerOpen = false;
        ResumableRun = null;

        if (resumeRunId is { } rid)
            await PreloadFinishedItemsAsync(rid);

        _cts = new CancellationTokenSource();
        _pause = new PauseTokenSource();
        var cancellationToken = _cts.Token;
        var pauseToken = _pause.Token;
        var orchestrator = _services.GetRequiredService<SyncOrchestrator>();
        var sink = new QueueProgress(_inbox);

        _runClock.Restart();
        _uiTimer.Start();
        try
        {
            await Task.Run(() => orchestrator.RunAsync(sink, cancellationToken, resumeRunId, pauseToken), CancellationToken.None);
            DrainInbox();

            var withFailures = FailedCount > 0;
            EtaText = "—";
            State = withFailures ? RunState.CompletedWithFailures : RunState.Completed;
            StateText = withFailures ? "تکمیل با خطا" : "تکمیل شد";
            PhaseText = $"{Processed:N0} رکورد در {Display.Duration(_runClock.Elapsed)} پردازش شد.";
            _notifications.Show(
                withFailures ? "همگام‌سازی با خطا تمام شد" : "همگام‌سازی کامل شد",
                $"ارسال: {SentCount:N0} • بدون تغییر: {DuplicateCount:N0} • ناموفق: {FailedCount:N0}",
                withFailures ? NotificationKind.Warning : NotificationKind.Success,
                forceToast: true);
            if (withFailures)
                ActiveFilter = ResultFilter.Failed;
        }
        catch (OperationCanceledException)
        {
            DrainInbox();
            State = RunState.Stopped;
            StateText = "متوقف شد";
            PhaseText = "اجرا توسط کاربر متوقف شد. نتایج ذخیره شده و می‌توانید بعداً آن را ادامه دهید.";
        }
        catch (Exception ex)
        {
            DrainInbox();
            State = RunState.Failed;
            StateText = "خطا";
            var (title, message) = Describe(ex);
            PhaseText = title;
            ShowBanner(title, message, InfoBarSeverity.Error);
            _notifications.Show(title, message, NotificationKind.Error, forceToast: true);
            if (ex is not (ConcurrentRunDetectedException or SyncConfigurationException or SchemaOutOfDateException or ReceiverConfigurationException))
                _logger.Error(ex, "Sync run failed");
        }
        finally
        {
            _uiTimer.Stop();
            _runClock.Stop();
            _cts.Dispose();
            _cts = null;
            _pause = null;
            await RefreshResumableAsync();
        }
    }

    private static (string Title, string Message) Describe(Exception ex) => ex switch
    {
        ConcurrentRunDetectedException => ("اجرای هم‌زمان", ex.Message),
        SyncConfigurationException => ("پیکربندی نامعتبر", ex.Message),
        SchemaOutOfDateException => ("ساختار پایگاه‌داده به‌روز نیست", ex.Message),
        ReceiverConfigurationException => ("سرویس دریافت‌کننده درخواست‌ها را رد می‌کند", ex.Message + " اجرا متوقف شد و قابل ادامه است."),
        RunLockLostException => ("قفل اجرا از دست رفت", ex.Message),
        _ => ("همگام‌سازی متوقف شد", $"{ex.Message}\nپیشرفت تا این لحظه ذخیره شده و قابل ادامه است.")
    };

    private async Task PreloadFinishedItemsAsync(Guid runId)
    {
        try
        {
            var items = await Task.Run(() => _staging.GetItemsAsync(runId, null, CancellationToken.None));
            foreach (var item in items.Where(i => i.Status != StagingItemStatus.Pending).OrderByDescending(i => i.UpdatedAtUtc))
                Results.Add(RecordResultItem.FromStaging(item, ++_sequence));
            RefreshFilter();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not preload finished items of run {RunId}", runId);
        }
    }

    private void ResetRun()
    {
        while (_inbox.TryDequeue(out _)) { }
        Results.Clear();
        ThroughputPoints.Clear();
        _rateWindow.Clear();
        _sequence = 0;
        _lastSampleSecond = -1;
        _sessionStartProcessed = -1;
        _lastPauseKind = SyncPauseKind.None;
        SelectedResult = null;
        ActiveFilter = ResultFilter.All;
        Total = Processed = SentCount = DuplicateCount = FailedCount = 0;
        ProgressPercent = 0;
        Throughput = 0;
        CurrentPerson = string.Empty;
        ElapsedText = "00:00";
        EtaText = "—";
        CurrentRunId = null;
        _sentValue.Value = _duplicateValue.Value = _failedValue.Value = 0;
        VisibleCount = 0;
    }


    private void DrainInbox()
    {
        SyncProgress? latest = null;
        var addedVisible = 0;

        while (_inbox.TryDequeue(out var p))
        {
            if (p.Phase == SyncPhase.Sending) latest = p;
            if (p.RunId is { } runId) CurrentRunId = runId;

            if (p.IsRecordResult)
            {
                var item = RecordResultItem.FromProgress(p, ++_sequence);
                Results.Insert(0, item);
                if (IsVisible(item)) addedVisible++;
            }

            ApplyPhaseAndPause(p);
        }

        if (latest is not null)
            ApplyCounters(latest);

        if (addedVisible > 0)
            VisibleCount += addedVisible;

        UpdateClock();
    }

    private void ApplyPhaseAndPause(SyncProgress p)
    {
        if (State is RunState.Stopping) return;

        switch (p.Phase)
        {
            case SyncPhase.Preparing:
                PhaseText = "در حال بررسی قفل اجرا و ساختار پایگاه‌داده…";
                return;
            case SyncPhase.LoadingSource when !p.IsPaused:
                PhaseText = "در حال خواندن رکوردها از پایگاه‌داده مبدأ…";
                return;
            case SyncPhase.Staging:
                PhaseText = $"در حال ثبت فهرست {p.Total:N0} رکورد برای این اجرا…";
                Total = p.Total;
                return;
            case SyncPhase.Finalizing:
                PhaseText = "در حال ثبت نهایی نتایج…";
                return;
        }

        if (p.IsPaused)
        {
            if (p.PauseKind != _lastPauseKind)
            {
                if (p.PauseKind == SyncPauseKind.User)
                {
                    State = RunState.Paused;
                    StateText = "متوقف موقت";
                }
                else
                {
                    State = RunState.WaitingForConnection;
                    StateText = p.PauseKind == SyncPauseKind.Connectivity ? "در انتظار اتصال" : "تلاش مجدد";
                    if (p.PauseKind == SyncPauseKind.Connectivity)
                    {
                        _notifications.Show("اتصال قطع شد", p.PauseMessage ?? "در انتظار برقراری مجدد اتصال…", NotificationKind.Warning);
                        ShowBanner("اتصال قطع شده است", p.PauseMessage ?? string.Empty, InfoBarSeverity.Warning);
                    }
                }
            }
            PhaseText = p.PauseMessage ?? PhaseText;
            _lastPauseKind = p.PauseKind;
            return;
        }

        if (_lastPauseKind == SyncPauseKind.Connectivity)
        {
            _notifications.Show("اتصال برقرار شد", "همگام‌سازی به‌صورت خودکار ادامه یافت.", NotificationKind.Success);
            IsBannerOpen = false;
        }

        if (State is RunState.Preparing or RunState.Paused or RunState.WaitingForConnection)
        {
            State = RunState.Running;
            StateText = "در حال اجرا";
        }
        _lastPauseKind = SyncPauseKind.None;

        if (State == RunState.Running)
            PhaseText = string.IsNullOrEmpty(p.CurrentPerson) ? "در حال ارسال…" : $"در حال پردازش: {p.CurrentPerson} ({p.CurrentPerId})";
    }

    private void ApplyCounters(SyncProgress p)
    {
        if (_sessionStartProcessed < 0) _sessionStartProcessed = p.Processed - (p.IsRecordResult ? 1 : 0);

        Total = p.Total;
        Processed = p.Processed;
        SentCount = p.SentCount;
        DuplicateCount = p.DuplicateCount;
        FailedCount = p.FailedCount;
        CurrentPerson = p.CurrentPerson;
        ProgressPercent = p.Total == 0 ? 0 : Math.Min(100, 100.0 * p.Processed / p.Total);

        _sentValue.Value = p.SentCount;
        _duplicateValue.Value = p.DuplicateCount;
        _failedValue.Value = p.FailedCount;
    }

    private void UpdateClock()
    {
        var elapsed = _runClock.Elapsed;
        ElapsedText = Display.Duration(elapsed);

        var second = Math.Floor(elapsed.TotalSeconds);
        if (second <= _lastSampleSecond || _sessionStartProcessed < 0) return;
        _lastSampleSecond = second;

        var sessionProcessed = Processed - _sessionStartProcessed;
        _rateWindow.Enqueue((elapsed.TotalSeconds, sessionProcessed));
        while (_rateWindow.Count > 6) _rateWindow.Dequeue();
        var (t0, p0) = _rateWindow.Peek();
        var rate = elapsed.TotalSeconds - t0 > 0.5 ? (sessionProcessed - p0) / (elapsed.TotalSeconds - t0) : 0;
        if (State is RunState.Paused or RunState.WaitingForConnection) rate = 0;
        Throughput = Math.Round(rate, 1);

        ThroughputPoints.Add(new ObservablePoint(second, Throughput));
        if (ThroughputPoints.Count > MaxThroughputPoints) ThroughputPoints.RemoveAt(0);

        var avg = elapsed.TotalSeconds > 1 ? sessionProcessed / elapsed.TotalSeconds : 0;
        var remaining = Total - Processed;
        EtaText = avg > 0.01 && remaining > 0 ? Display.Duration(TimeSpan.FromSeconds(remaining / avg)) : "—";
    }


    private bool IsVisible(RecordResultItem item) =>
        item.Matches(ActiveFilter) &&
        (string.IsNullOrWhiteSpace(SearchText) || item.SearchKey.Contains(SearchText.Trim().ToLowerInvariant(), StringComparison.Ordinal));

    private void RefreshFilter()
    {
        ResultsView.Refresh();
        VisibleCount = ActiveFilter == ResultFilter.All && string.IsNullOrWhiteSpace(SearchText)
            ? Results.Count
            : Results.Count(IsVisible);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnActiveFilterChanged(ResultFilter value) => RefreshFilter();

    partial void OnSelectedResultChanged(RecordResultItem? value)
    {
        Detail = value is null ? null : RecordDetailViewModel.From(value);
        IsDetailOpen = value is not null;
    }

    private void ShowBanner(string title, string message, InfoBarSeverity severity)
    {
        BannerTitle = title;
        BannerMessage = message;
        BannerSeverity = severity;
        IsBannerOpen = true;
    }

    private void ApplyChartTheme()
    {
        var dark = _theme.IsDark;
        var label = dark ? SKColor.Parse("#A1A8B3") : SKColor.Parse("#5B6472");
        var grid = dark ? new SKColor(255, 255, 255, 24) : new SKColor(0, 0, 0, 20);
        foreach (var axis in XAxes.Concat(YAxes))
            axis.LabelsPaint = new SolidColorPaint(label);
        YAxes[0].SeparatorsPaint = new SolidColorPaint(grid, 1);
        XAxes[0].SeparatorsPaint = null;
    }

    private sealed class QueueProgress(ConcurrentQueue<SyncProgress> queue) : IProgress<SyncProgress>
    {
        public void Report(SyncProgress value) => queue.Enqueue(value);
    }

    public Task StopAndWaitAsync()
    {
        if (!IsBusy) return Task.CompletedTask;
        Stop();
        var tcs = new TaskCompletionSource();
        PropertyChangedEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (e.PropertyName == nameof(State) && !IsBusy)
            {
                PropertyChanged -= handler;
                tcs.TrySetResult();
            }
        };
        PropertyChanged += handler;
        return tcs.Task;
    }
}
