using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapna.Sender.Services;
using Wpf.Ui.Controls;

namespace Mapna.Sender.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppThemeService _theme;

    public MainWindowViewModel(AppThemeService theme, DashboardViewModel dashboard)
    {
        _theme = theme;
        Dashboard = dashboard;
        _theme.ThemeChanged += (_, _) => OnPropertyChanged(nameof(ThemeSymbol));
    }

    public string ApplicationTitle => "مپنا · همگام‌سازی اطلاعات پرسنلی";

    public DashboardViewModel Dashboard { get; }

    public SymbolRegular ThemeSymbol => _theme.IsDark ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24;

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();
}
