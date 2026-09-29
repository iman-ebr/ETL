using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapna.Sender.Staging;
using Wpf.Ui.Controls;

namespace Mapna.Sender.ViewModels;

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

/// <summary>Past runs (dbo.SyncRuns) and their per-record results (dbo.SyncItems).</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly SqlStagingRepository _staging;
    private bool _loadedOnce;

    public HistoryViewModel(SqlStagingRepository staging)
    {
        _staging = staging;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => o is RecordResultItem item && item.Matches(ActiveFilter);
    }

    public ObservableCollection<RunSummary> Runs { get; } = [];
    public ObservableCollection<RecordResultItem> Items { get; } = [];
    public ICollectionView ItemsView { get; }

    [ObservableProperty] public partial RunSummary? SelectedRun { get; set; }
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
            var runs = await Task.Run(() => _staging.GetRecentRunsAsync(50, CancellationToken.None));
            Runs.Clear();
            foreach (var run in runs) Runs.Add(new RunSummary(run));
            SelectedRun ??= Runs.FirstOrDefault();
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

    async partial void OnSelectedRunChanged(RunSummary? value)
    {
        Items.Clear();
        if (value is null) return;
        try
        {
            var items = await Task.Run(() => _staging.GetItemsAsync(value.Run.RunId, null, CancellationToken.None));
            if (SelectedRun != value) return;   // user clicked another run meanwhile
            var seq = 0;
            foreach (var item in items.OrderByDescending(i => i.UpdatedAtUtc))
                Items.Add(RecordResultItem.FromStaging(item, ++seq));
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
    }
}
