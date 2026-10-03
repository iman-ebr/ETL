using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Mapna.Sender.Services;

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error
}

public interface INotificationService
{
    /// <summary>In-app snackbar always; a native Windows toast too when the window isn't in the foreground (or <paramref name="forceToast"/>).</summary>
    void Show(string title, string message, NotificationKind kind, bool forceToast = false);
}

public sealed class NotificationService(ISnackbarService snackbar, IConfiguration configuration, ILogger logger) : INotificationService
{
    private readonly bool _toastsEnabled = configuration.GetValue("Ui:EnableToasts", true);

    public void Show(string title, string message, NotificationKind kind, bool forceToast = false)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        dispatcher.Invoke(() =>
        {
            var (appearance, symbol) = kind switch
            {
                NotificationKind.Success => (ControlAppearance.Success, SymbolRegular.CheckmarkCircle24),
                NotificationKind.Warning => (ControlAppearance.Caution, SymbolRegular.Warning24),
                NotificationKind.Error => (ControlAppearance.Danger, SymbolRegular.ErrorCircle24),
                _ => (ControlAppearance.Info, SymbolRegular.Info24)
            };

            if (snackbar.GetSnackbarPresenter() is not null)
                snackbar.Show(title, message, appearance, new SymbolIcon(symbol), TimeSpan.FromSeconds(kind == NotificationKind.Error ? 8 : 5));

            var windowActive = Application.Current?.MainWindow?.IsActive == true;
            if (_toastsEnabled && (forceToast || !windowActive))
                ShowToast(title, message);
        });
    }

    private void ShowToast(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            // Toasts can be disabled by policy (Focus Assist, GPO). Never let that break a sync.
            logger.Debug(ex, "Could not show Windows toast");
        }
    }
}
