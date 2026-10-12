using System.Windows;
using WebService.Sender.Views;
using WebService.Sender.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebService.Sender.Services;

public sealed class ApplicationHostService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Application.Current.Windows.OfType<MainWindow>().Any())
            return Task.CompletedTask;

        var window = services.GetRequiredService<MainWindow>();
        services.GetRequiredService<AppThemeService>().Initialize(window);
        window.Show();
        window.Navigate(typeof(DashboardPage));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
