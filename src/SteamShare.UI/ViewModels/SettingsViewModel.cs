using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using SteamShare.Core.Localization;
using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.UI.Services;

namespace SteamShare.UI.ViewModels;

/// <summary>
/// ViewModel for the Settings section.
/// Manages language, theme, download directory, and auto-track interval.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private static readonly ILogger LogSerilog = Log.ForContext<SettingsViewModel>();
    private readonly IConfigService _configService;
    private readonly ThemeService _themeService;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly LocalizationService _loc;

    [ObservableProperty]
    private string _language;

    [ObservableProperty]
    private string _theme;

    [ObservableProperty]
    private string _downloadDirectory;

    [ObservableProperty]
    private int _autoTrackInterval;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _autoRestartPendingTasks;

    /// <summary>Available language options.</summary>
    public ObservableCollection<LocalizedOption> Languages { get; } = new();

    /// <summary>Available theme options.</summary>
    public ObservableCollection<LocalizedOption> Themes { get; } = new();

    public SettingsViewModel(
        IConfigService configService,
        ThemeService themeService,
        INotificationService notificationService,
        IDialogService dialogService,
        LocalizationService loc)
    {
        _configService = configService;
        _themeService = themeService;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _loc = loc;

        // Load current settings
        var config = _configService.Current;
        _language = config.Language;
        _theme = config.Theme;
        _downloadDirectory = config.DownloadDirectory ?? string.Empty;
        _autoTrackInterval = config.AutoTrackIntervalSeconds;
        _autoRestartPendingTasks = config.AutoRestartPendingTasks;

        // Populate dropdown options with localized display names
        Languages.Add(new LocalizedOption("en-US", _loc.GetString("Language_enUS")));
        Languages.Add(new LocalizedOption("zh-CN", _loc.GetString("Language_zhCN")));
        Themes.Add(new LocalizedOption("System", _loc.GetString("Theme_System")));
        Themes.Add(new LocalizedOption("Light", _loc.GetString("Theme_Light")));
        Themes.Add(new LocalizedOption("Dark", _loc.GetString("Theme_Dark")));

        LogSerilog.Debug("SettingsViewModel created: lang={Language}, theme={Theme}", Language, Theme);
    }

    /// <summary>Saves the current settings to disk and applies the theme immediately.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsSaving = true;

            LogSerilog.Information("Saving settings: lang={Language}, theme={Theme}",
                Language, Theme);

            if (_configService is ConfigService configSvc)
            {
                configSvc.Current = new AppConfig
                {
                    Language = Language,
                    Theme = Theme,
                    DownloadDirectory = string.IsNullOrWhiteSpace(DownloadDirectory) ? null : DownloadDirectory,
                    AutoTrackIntervalSeconds = AutoTrackInterval,
                    Version = _configService.Current.Version,
                    DisclaimerDismissed = _configService.Current.DisclaimerDismissed,
                    SteamPath = _configService.Current.SteamPath,
                    AutoRestartPendingTasks = AutoRestartPendingTasks,
                };
            }

            await _configService.SaveAsync();

            _loc.SetCulture(Language);

            // Apply theme immediately
            _themeService.ApplyTheme(Theme);

            LogSerilog.Information("Settings saved");
            _notificationService.ShowInfo(_loc.GetString("Notification_SettingsSaved"));
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Failed to save settings: {Message}", ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_SettingsSaveError", ex.Message));
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Opens a folder picker to select the download directory.</summary>
    [RelayCommand]
    private async Task BrowseDownloadDirectoryAsync()
    {
        var folderPath = await _dialogService.PickFolderAsync();
        if (folderPath is not null)
        {
            DownloadDirectory = folderPath;
        }
    }

    /// <summary>Opens the application log file directory in the OS file explorer.</summary>
    [RelayCommand]
    private void OpenLogFile()
    {
        var logDir = Path.Combine(_configService.ConfigDirectory, "logs");
        if (Directory.Exists(logDir))
        {
            _dialogService.OpenFolder(logDir);
        }
    }
}
