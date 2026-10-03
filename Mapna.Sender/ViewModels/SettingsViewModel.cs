using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapna.Sender.Services;
using Microsoft.Data.SqlClient;

namespace Mapna.Sender.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppThemeService _theme;
    private readonly ConnectivityProbe _probe;
    private readonly AppSettings _settings;

    public SettingsViewModel(AppThemeService theme, ConnectivityProbe probe, AppSettings settings)
    {
        _theme = theme;
        _probe = probe;
        _settings = settings;
    }

    public bool IsSystemTheme
    {
        get => _theme.Preference == ThemePreference.System;
        set { if (value) SetTheme(ThemePreference.System); }
    }

    public bool IsLightTheme
    {
        get => _theme.Preference == ThemePreference.Light;
        set { if (value) SetTheme(ThemePreference.Light); }
    }

    public bool IsDarkTheme
    {
        get => _theme.Preference == ThemePreference.Dark;
        set { if (value) SetTheme(ThemePreference.Dark); }
    }

    public string SourceDatabase => Describe(_settings.SourceConnectionString);
    public string AppDatabase => Describe(_settings.AppConnectionString);
    public string ReceiverUrl => _settings.ReceiverApiBaseUrl;
    public string LogsDirectory => _settings.LogsDirectory;
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";

    [ObservableProperty] public partial string? SourceStatus { get; set; }
    [ObservableProperty] public partial bool? SourceOk { get; set; }
    [ObservableProperty] public partial string? AppStatus { get; set; }
    [ObservableProperty] public partial bool? AppOk { get; set; }
    [ObservableProperty] public partial string? ReceiverStatus { get; set; }
    [ObservableProperty] public partial bool? ReceiverOk { get; set; }
    [ObservableProperty] public partial bool IsTesting { get; set; }

    [RelayCommand]
    private async Task TestConnectionsAsync()
    {
        IsTesting = true;
        SourceOk = AppOk = ReceiverOk = null;
        SourceStatus = AppStatus = ReceiverStatus = "در حال بررسی…";
        try
        {
            var source = Task.Run(() => _probe.CheckSourceDatabaseAsync());
            var app = Task.Run(() => _probe.CheckAppDatabaseAsync());
            var receiver = Task.Run(() => _probe.CheckReceiverAsync());
            await Task.WhenAll(source, app, receiver);

            (SourceOk, SourceStatus) = (source.Result.Ok, source.Result.Message);
            var appResult = app.Result;
            AppOk = appResult.IsReady;
            AppStatus = appResult.IsReady ? "در دسترس — ساختار به‌روز است"
                : appResult.PendingMigrations.Count > 0 ? "Migration معوق: " + string.Join(", ", appResult.PendingMigrations)
                : appResult.Error;
            (ReceiverOk, ReceiverStatus) = (receiver.Result.Ok, receiver.Result.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_settings.LogsDirectory);
        Process.Start(new ProcessStartInfo { FileName = _settings.LogsDirectory, UseShellExecute = true });
    }

    private void SetTheme(ThemePreference preference)
    {
        _theme.SetPreference(preference);
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    /// <summary>Server and database only; credentials in a connection string are never shown.</summary>
    private static string Describe(string connectionString)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            return $"{b.DataSource} / {b.InitialCatalog}";
        }
        catch
        {
            return "—";
        }
    }
}
