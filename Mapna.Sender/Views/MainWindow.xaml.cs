using System.ComponentModel;
using Mapna.Sender.Services;
using Mapna.Sender.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace Mapna.Sender.Views;

public partial class MainWindow : FluentWindow
{
    private readonly IDialogService _dialogs;
    private bool _closeConfirmed;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        IDialogService dialogs)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _dialogs = dialogs;

        InitializeComponent();

        RootNavigation.SetPageProviderService(pageProvider);
        navigationService.SetNavigationControl(RootNavigation);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);
    }

    public void Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    /// <summary>Never let the window vanish in the middle of a run: confirm, stop gracefully, then close.</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeConfirmed || !ViewModel.Dashboard.IsBusy)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        var confirmed = await _dialogs.ConfirmAsync(
            "همگام‌سازی در حال اجراست",
            "با خروج، اجرا پس از رکورد جاری متوقف و نتایج ذخیره می‌شود. بعداً می‌توانید آن را ادامه دهید. خارج می‌شوید؟",
            "توقف و خروج");
        if (!confirmed) return;

        await ViewModel.Dashboard.StopAndWaitAsync();
        _closeConfirmed = true;
        Close();
    }
}
