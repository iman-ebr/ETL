using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapna.Contracts;
using Mapna.Sender.Services;

namespace Mapna.Sender.ViewModels;

public sealed class ExplorerRow
{
    public required PersonnelRecord Record { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public bool IsValid => Errors.Count == 0;
    public string FullName => $"{Record.PerName} {Record.PerSurname}".Trim();
    public string ErrorSummary => string.Join(" • ", Errors);
    public string SearchKey { get; init; } = string.Empty;
}

/// <summary>Read-only view of the source table, with each record's validation result, before anything is sent.</summary>
public partial class ExplorerViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IFileExportService _export;
    private readonly INotificationService _notifications;
    private readonly DispatcherTimer _searchDebounce;
    private bool _loadedOnce;

    public ExplorerViewModel(AppSettings settings, IFileExportService export, INotificationService notifications)
    {
        _settings = settings;
        _export = export;
        _notifications = notifications;
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is ExplorerRow r && IsVisible(r);
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); Refresh(); };
    }

    public ObservableCollection<ExplorerRow> Rows { get; } = [];
    public ICollectionView RowsView { get; }

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasLoadError))] public partial string? LoadError { get; set; }
    public bool HasLoadError => LoadError is not null;
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ShowInvalidOnly { get; set; }
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial int InvalidCount { get; set; }
    [ObservableProperty] public partial int VisibleCount { get; set; }
    [ObservableProperty] public partial ExplorerRow? Selected { get; set; }
    [ObservableProperty] public partial IReadOnlyList<FieldRow> SelectedFields { get; set; } = [];
    [ObservableProperty] public partial bool IsDetailOpen { get; set; }

    public async Task OnNavigatedToAsync()
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var rows = await Task.Run(async () =>
            {
                var validator = new PersonnelValidator();
                var records = await new SourceRepository(_settings.SourceConnectionString, _settings.NormalizeArabicLetters).GetAllPersonnelAsync();
                var duplicateIds = records.GroupBy(r => r.PerId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                return records.Select(r =>
                {
                    var errors = validator.Validate(r).Errors.Select(e => e.ErrorMessage).ToList();
                    if (duplicateIds.Contains(r.PerId)) errors.Add("PerId در جدول مبدأ تکراری است.");
                    return new ExplorerRow
                    {
                        Record = r,
                        Errors = errors,
                        SearchKey = $"{r.PerId} {r.PerName} {r.PerSurname} {r.NationalCode} {r.MobileNo}".ToLowerInvariant()
                    };
                }).OrderBy(r => r.Record.PerId).ToList();
            });

            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
            TotalCount = rows.Count;
            InvalidCount = rows.Count(r => !r.IsValid);
            Refresh();
        }
        catch (Exception ex)
        {
            LoadError = $"خواندن داده از پایگاه‌داده مبدأ ناموفق بود: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var rows = Rows.Where(IsVisible).ToList();
        var path = await _export.ExportCsvAsync($"source-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        [
            ("PerId", r => r.Record.PerId),
            ("نام", r => r.Record.PerName),
            ("نام خانوادگی", r => r.Record.PerSurname),
            ("کد ملی", r => r.Record.NationalCode),
            ("موبایل", r => r.Record.MobileNo),
            ("ایمیل", r => r.Record.PerEmail),
            ("معتبر", r => r.IsValid ? "بله" : "خیر"),
            ("خطاها", r => r.ErrorSummary)
        ], rows);
        if (path is not null)
            _notifications.Show("خروجی ذخیره شد", $"{rows.Count:N0} رکورد ذخیره شد.", NotificationKind.Success);
    }

    [RelayCommand]
    private void CloseDetail() => Selected = null;

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnShowInvalidOnlyChanged(bool value) => Refresh();

    partial void OnSelectedChanged(ExplorerRow? value)
    {
        SelectedFields = value is null ? [] : RecordDetailViewModel.FromRecord(value.Record);
        IsDetailOpen = value is not null;
    }

    private bool IsVisible(ExplorerRow r) =>
        (!ShowInvalidOnly || !r.IsValid) &&
        (string.IsNullOrWhiteSpace(SearchText) || r.SearchKey.Contains(SearchText.Trim().ToLowerInvariant(), StringComparison.Ordinal));

    private void Refresh()
    {
        RowsView.Refresh();
        VisibleCount = Rows.Count(IsVisible);
    }
}
