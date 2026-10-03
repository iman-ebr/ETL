using System.Windows.Controls;
using Mapna.Sender.ViewModels;

namespace Mapna.Sender.Views.Pages;

public partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.OnNavigatedToAsync();
    }
}
