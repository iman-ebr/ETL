using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebService.Sender.Services;
using WebService.Sender.Staging;

namespace WebService.Sender.ViewModels;

public sealed class OutstandingRow
{
    public required int PerId { get; init; }
    public required string PersonName { get; init; }
    public required FailureCategory Category { get; init; }
    public required string Message { get; init; }
    public required int AttemptCount { get; init; }

    public string CategoryText => FailureInfo.CategoryText(Category);
    public string Advice => FailureInfo.Advice(Category);
    public bool IsNotSent => Category == FailureCategory.NotSent;
}

public sealed record CategoryCount(string Text, int Count);

public sealed partial class RunReportViewModel : ObservableObject
{
    private readonly IFileExportService _export;
    private readonly INotificationService _notifications;
    private readonly Guid _runId;

    public RunReportViewModel(StagingRun run, IReadOnlyList<OutstandingItem> outstanding, bool afterRun,
        IFileExportService export, INotificationService notifications)
    {
        _export = export;
        _notifications = notifications;
        _runId = run.RunId;

        Total = run.TotalCount;
        SentCount = run.SentCount;
        DuplicateCount = run.DuplicateCount;
        FailedCount = run.FailedCount;
        OutstandingCount = outstanding.Count;
        var outstandingFailed = outstanding.Count(o => o.Status != StagingItemStatus.Pending);
        ResolvedLaterCount = Math.Max(0, run.FailedCount - outstandingFailed);
        NotSentCount = outstanding.Count(o => o.Status == StagingItemStatus.Pending);
        IsRetryRun = run.ParentRunId is not null;

        foreach (var o in outstanding)
        {
            var category = FailureInfo.Classify(o.Status, o.Reason);
            Rows.Add(new OutstandingRow
            {
                PerId = o.PerId,
                PersonName = o.PersonName,
                Category = category,
                Message = FailureInfo.Message(o.Status, o.Reason),
                AttemptCount = o.AttemptCount
            });
        }

        Categories = Rows.GroupBy(r => r.Category)
            .OrderByDescending(g => g.Count())
            .Select(g => new CategoryCount(FailureInfo.CategoryText(g.Key), g.Count()))
            .ToList();

        NeedsSourceFixCount = Rows.Count(r => r.Category is FailureCategory.InvalidData or FailureCategory.DuplicateInSource
            or FailureCategory.NationalCodeConflict or FailureCategory.RemovedFromSource);

        Title = afterRun ? "گزارش پایان اجرا" : "رکوردهای ناموفق این اجرا";
        Headline = OutstandingCount == 0
            ? "همه‌ی رکوردها با موفقیت ارسال شده‌اند."
            : $"{OutstandingCount:N0} رکورد از {Total:N0} رکورد ارسال نشد.";
        RunLabel = $"{Display.Date(run.StartedAtUtc)} · {run.RunId.ToString()[..8]}";
    }

    public string Title { get; }
    public string Headline { get; }
    public string RunLabel { get; }
    public bool IsRetryRun { get; }

    public int Total { get; }
    public int SentCount { get; }
    public int DuplicateCount { get; }
    public int FailedCount { get; }
    public int OutstandingCount { get; }
    public int NotSentCount { get; }
    public int ResolvedLaterCount { get; }
    public int NeedsSourceFixCount { get; }

    public bool HasResolvedLater => ResolvedLaterCount > 0;
    public bool HasSourceFixWarning => NeedsSourceFixCount > 0;
    public string SourceFixWarning =>
        $"{NeedsSourceFixCount:N0} رکورد به‌خاطر خود داده رد شده‌اند؛ تا داده در مبدأ اصلاح نشود، ارسال مجدد دوباره همان خطا را می‌دهد.";
    public string ResolvedLaterText => $"{ResolvedLaterCount:N0} رکورد ناموفق این اجرا بعداً با ارسال مجدد برطرف شده‌اند و در فهرست نیستند.";

    public ObservableCollection<OutstandingRow> Rows { get; } = [];
    public IReadOnlyList<CategoryCount> Categories { get; }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = await _export.ExportCsvAsync($"failed-{_runId.ToString()[..8]}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        [
            ("PerId", r => r.PerId),
            ("نام", r => r.PersonName),
            ("نوع مشکل", r => r.CategoryText),
            ("دلیل", r => r.Message),
            ("راهنما", r => r.Advice),
            ("دفعات تلاش", r => r.AttemptCount)
        ], Rows);

        if (path is not null)
            _notifications.Show("خروجی ذخیره شد", $"{Rows.Count:N0} رکورد در {System.IO.Path.GetFileName(path)} ذخیره شد.", NotificationKind.Success);
    }
}