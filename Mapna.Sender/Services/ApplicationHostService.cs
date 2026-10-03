using System.Windows;
using Mapna.Sender.Views;
using Mapna.Sender.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mapna.Sender.Services;

/// <summary>Shows the shell once the Generic Host has started. All UI objects come from DI.</summary>
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
