using System.Windows;
using System.Windows.Threading;
using WebService.Sender.Logging;
using WebService.Sender.Services;
using WebService.Sender.Staging;
using WebService.Sender.ViewModels;
using WebService.Sender.Views;
using WebService.Sender.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace WebService.Sender;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory
        });

        var settings = AppSettings.FromConfiguration(builder.Configuration);
        var logger = LoggingSetup.CreateLogger(settings.LogsDirectory);
        Log.Logger = logger;

        var services = builder.Services;
        services.AddSingleton(settings);
        services.AddSingleton<ILogger>(logger);
        services.AddSingleton(sp => new SqlStagingRepository(settings.AppConnectionString, logger));
        services.AddTransient<SyncOrchestrator>();

        services.AddNavigationViewPageProvider();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        services.AddHostedService<ApplicationHostService>();
        services.AddSingleton<AppThemeService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFileExportService, FileExportService>();
        services.AddSingleton<ConnectivityProbe>();
        services.AddSingleton<IRunReportDialogService, RunReportDialogService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DashboardPage>();
        services.AddSingleton<ExplorerViewModel>();
        services.AddSingleton<ExplorerPage>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<HistoryPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsPage>();

        _host = builder.Build();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.Fatal(args.ExceptionObject as Exception, "Unhandled exception - process is terminating (IsTerminating={IsTerminating})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        logger.Information("WebService.Sender (WPF) starting up");
        try
        {
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Application failed to start");
            MessageBox.Show($"برنامه نتوانست اجرا شود:\n{ex.Message}", "خطای راه‌اندازی",
                MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            Shutdown(1);
        }
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.Information("WebService.Sender shutting down");
        await Log.CloseAndFlushAsync();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI thread exception");
        _host?.Services.GetService<INotificationService>()?.Show(
            "خطای غیرمنتظره", e.Exception.Message, NotificationKind.Error);
        e.Handled = true;
    }
}
