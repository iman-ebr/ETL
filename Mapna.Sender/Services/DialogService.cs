using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Mapna.Sender.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText = "انصراف");
}

/// <summary>Fluent (WPF-UI) message box instead of the legacy Win32 MessageBox.</summary>
public sealed class DialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText = "انصراف")
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            PrimaryButtonAppearance = ControlAppearance.Danger,
            FlowDirection = FlowDirection.RightToLeft,
            Owner = Application.Current.MainWindow
        };
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }
}
