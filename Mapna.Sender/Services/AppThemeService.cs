using System.IO;
using System.Text.Json;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Mapna.Sender.Services;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>
/// Follows the Windows light/dark setting by default (SystemThemeWatcher), with a manual override
/// persisted per user in %LOCALAPPDATA%\Mapna.Sender\ui.json.
/// </summary>
public sealed class AppThemeService
{
    private static readonly string PreferenceFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mapna.Sender", "ui.json");

    private Window? _window;

    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    public ApplicationTheme CurrentTheme => ApplicationThemeManager.GetAppTheme();

    public bool IsDark => CurrentTheme == ApplicationTheme.Dark;

    public event EventHandler? ThemeChanged;

    public AppThemeService()
    {
        ApplicationThemeManager.Changed += (_, _) => ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Initialize(Window window)
    {
        _window = window;
        Preference = LoadPreference();
        Apply(Preference);
    }

    public void SetPreference(ThemePreference preference)
    {
        Preference = preference;
        SavePreference(preference);
        Apply(preference);
    }

    /// <summary>Quick toggle from the title bar: flips to the opposite of what is shown now (and stops following Windows).</summary>
    public void Toggle() => SetPreference(IsDark ? ThemePreference.Light : ThemePreference.Dark);

    private void Apply(ThemePreference preference)
    {
        if (_window is null) return;

        var theme = preference switch
        {
            ThemePreference.Dark => ApplicationTheme.Dark,
            ThemePreference.Light => ApplicationTheme.Light,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light
        };
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);

        // SystemThemeWatcher needs the window's HWND: Watch/UnWatch before Loaded throws. That exception once
        // meant a user who picked "Dark" could never open the app again (it died during startup with no window).
        if (_window.IsLoaded)
            UpdateWatcher(preference);
        else
            _window.Loaded += OnWindowLoaded;

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool _watching;

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ((Window)sender).Loaded -= OnWindowLoaded;
        UpdateWatcher(Preference);
    }

    private void UpdateWatcher(ThemePreference preference)
    {
        if (_window is null) return;
        if (preference == ThemePreference.System && !_watching)
        {
            SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, true);
            _watching = true;
        }
        else if (preference != ThemePreference.System && _watching)
        {
            SystemThemeWatcher.UnWatch(_window);
            _watching = false;
        }
    }

    private static ThemePreference LoadPreference()
    {
        try
        {
            if (File.Exists(PreferenceFile) &&
                JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(PreferenceFile)) is { } prefs &&
                Enum.TryParse<ThemePreference>(prefs.Theme, out var theme))
                return theme;
        }
        catch
        {
            // A corrupt preference file must never stop the app from starting.
        }
        return ThemePreference.System;
    }

    private static void SavePreference(ThemePreference preference)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferenceFile)!);
            File.WriteAllText(PreferenceFile, JsonSerializer.Serialize(new UiPreferences(preference.ToString())));
        }
        catch
        {
            // Non-critical.
        }
    }

    private sealed record UiPreferences(string Theme);
}
