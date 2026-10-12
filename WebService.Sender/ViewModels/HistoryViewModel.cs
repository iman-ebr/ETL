using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebService.Sender.Services;
using WebService.Sender.Staging;
using WebService.Sender.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace WebService.Sender.ViewModels;

public sealed class RunSummary(StagingRun run)
{
    public StagingRun Run { get; } = run;
    public string StartedText => Display.Date(Run.StartedAtUtc);
    public string DurationText => Run.CompletedAtUtc is { } end ? Display.Duration(end - Run.StartedAtUtc) : "—";
    public string StatusText => Display.RunStatusText(Run.Status);
    public string CountsText => $"{Run.ProcessedCount:N0} / {Run.TotalCount:N0}";
    public string ShortId => Run.RunId.ToString()[..8];

    public InfoBarSeverity Severity => Run.Status switch
    {
        RunStatus.Completed => InfoBarSeverity.Success,
        RunStatus.CompletedWithFailures or RunStatus.Paused => InfoBarSeverity.Warning,
        RunStatus.Crashed => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational
    };
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly SqlStagingRepository _staging;
    private readonly DashboardViewModel _dashboard;
    private readonly IRunReportDialogService _reports;
    private readonly INavigationService _navigation;
    private bool _loadedOnce;

    public HistoryViewModel(SqlStagingRepository staging, DashboardViewModel dashboard,
        IRunReportDialogService reports, INavigationService navigation)
    {
        _staging = staging;
        _dashboard = dashboard;
        _reports = reports;
        _navigation = navigation;

        _dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DashboardViewModel.IsBusy) or nameof(DashboardViewModel.IsEnvironmentReady))
                RetryOutstandingCommand.NotifyCanExecuteChanged();
        };
        _dashboard.RunFinished += async (_, _) =>
        {
            if (_loadedOnce) await RefreshAsync();
        };

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => o is RecordResultItem item && item.Matches(ActiveFilter);
    }

    public ObservableCollection<RunSummary> Runs { get; } = [];
    public ObservableCollection<RecordResultItem> Items { get; } = [];
    public ICollectionView ItemsView { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryOutstandingCommand))]
    public partial RunSummary? SelectedRun { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutstanding), nameof(RetryButtonText))]
    [NotifyCanExecuteChangedFor(nameof(RetryOutstandingCommand))]
    public partial int OutstandingCount { get; set; }

    public bool HasOutstanding => OutstandingCount > 0;
    public string RetryButtonText => $"ارسال مجدد ناموفق‌ها ({OutstandingCount:N0})";
    [ObservableProperty] public partial ResultFilter ActiveFilter { get; set; } = ResultFilter.All;
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? LoadError { get; set; }
    [ObservableProperty] public partial RecordResultItem? SelectedItem { get; set; }
    [ObservableProperty] public partial RecordDetailViewModel? Detail { get; set; }

    public async Task OnNavigatedToAsync()
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var keepRunId = SelectedRun?.Run.RunId;
            var runs = await Task.Run(() => _staging.GetRecentRunsAsync(50, CancellationToken.None));
            Runs.Clear();
            foreach (var run in runs) Runs.Add(new RunSummary(run));
            var reselect = keepRunId is { } id ? Runs.FirstOrDefault(r => r.Run.RunId == id) : null;
            SelectedRun = reselect ?? Runs.FirstOrDefault();
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SetFilter(ResultFilter filter) => ActiveFilter = filter;

    partial void OnActiveFilterChanged(ResultFilter value) => ItemsView.Refresh();

    partial void OnSelectedItemChanged(RecordResultItem? value) =>
        Detail = value is null ? null : RecordDetailViewModel.From(value);

    async partial void OnSelectedRunChanged(RunSummary? value) => await LoadSelectedRunAsync(value);

    private async Task LoadSelectedRunAsync(RunSummary? value)
    {
        Items.Clear();
        OutstandingCount = 0;
        if (value is null) return;
        try
        {
            var itemsTask = Task.Run(() => _staging.GetItemsAsync(value.Run.RunId, null, CancellationToken.None));
            var countTask = Task.Run(() => _staging.GetOutstandingCountAsync(value.Run.RunId, CancellationToken.None));
            var items = await itemsTask;
            var outstanding = await countTask;
            if (SelectedRun != value) return;  
            OutstandingCount = outstanding;
            var seq = 0;
            foreach (var item in items.OrderByDescending(i => i.UpdatedAtUtc))
                Items.Add(RecordResultItem.FromStaging(item, ++seq));
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
    }

    private bool CanRetryOutstanding() =>
        SelectedRun is { Run.Status: not RunStatus.Running } && OutstandingCount > 0 && _dashboard.IsIdle && _dashboard.IsEnvironmentReady;

    [RelayCommand(CanExecute = nameof(CanRetryOutstanding))]
    private async Task RetryOutstandingAsync()
    {
        if (SelectedRun is not { } run) return;
        if (!await _reports.ShowAsync(run.Run.RunId, afterRun: false)) return;

        _navigation.Navigate(typeof(DashboardPage));
        await _dashboard.RetryOutstandingAsync(run.Run.RunId);
    }
}