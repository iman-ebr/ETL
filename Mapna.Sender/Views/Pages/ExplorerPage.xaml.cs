using System.Windows.Controls;
using Mapna.Sender.ViewModels;

namespace Mapna.Sender.Views.Pages;

public partial class ExplorerPage : Page
{
    public ExplorerViewModel ViewModel { get; }

    public ExplorerPage(ExplorerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.OnNavigatedToAsync();
    }

    private void ToggleSwitch_Checked(object sender, System.Windows.RoutedEventArgs e)
    {

    }
}
