using System.Windows.Controls;
using Mapna.Sender.ViewModels;

namespace Mapna.Sender.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
