using WebService.Sender.Staging;
using WebService.Sender.ViewModels;
using WebService.Sender.Views.Dialogs;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
namespace WebService.Sender.Services;

public interface IRunReportDialogService
{
    Task<bool> ShowAsync(Guid runId, bool afterRun);
}

public sealed class RunReportDialogService(IContentDialogService dialogs, SqlStagingRepository staging,
    IFileExportService export, INotificationService notifications, ILogger logger) : IRunReportDialogService
{
    public async Task<bool> ShowAsync(Guid runId, bool afterRun)
    {
        StagingRun? run; List<OutstandingItem> outstanding;
        try
        {
            run = await Task.Run(() => staging.GetRunAsync(runId, CancellationToken.None));
            outstanding = await Task.Run(() => staging.GetOutstandingAsync(runId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not load the report of run {RunId}", runId);
            notifications.Show("خطا", $"گزارش اجرا خوانده نشد: {ex.Message}", NotificationKind.Error);
            return false;
        }
        if (run is null) return false;
        if (afterRun && outstanding.Count == 0) return false;

        var vm = new RunReportViewModel(run, outstanding, afterRun, export, notifications);
        var result = await dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = vm.Title,
            Content = new RunReportDialogContent { DataContext = vm },
            PrimaryButtonText = vm.OutstandingCount > 0 ? $"ارسال مجدد {vm.OutstandingCount:N0} رکورد" : string.Empty,
            CloseButtonText = "بستن"
        });
        return result == ContentDialogResult.Primary && vm.OutstandingCount > 0;
    }
}