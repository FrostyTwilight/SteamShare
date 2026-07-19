using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using SteamShare.Core.Localization;
using SteamShare.UI.Services;

namespace SteamShare.UI.ViewModels;

/// <summary>
/// Main shell ViewModel. Manages navigation state, Steam connection status,
/// and the first-run disclaimer banner.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private static readonly ILogger LogSerilog = Log.ForContext<MainViewModel>();
    private readonly INavigationService _navigationService;
    private readonly LocalizationService _loc;

    public MainViewModel(INavigationService navigationService, LocalizationService loc)
    {
        _navigationService = navigationService;
        _loc = loc;
        LogSerilog.Debug("MainViewModel created");
    }

    /// <summary>The currently displayed ViewModel. Bound to ContentControl.Content.</summary>
    [ObservableProperty]
    private ViewModelBase? _currentView;

    /// <summary>Whether Steam client is connected and the API is ready.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamStatusText))]
    private bool _isSteamConnected;

    /// <summary>Human-readable Steam connection status for the status bar.</summary>
    public string SteamStatusText => IsSteamConnected
        ? _loc.GetString("Status_SteamConnected")
        : _loc.GetString("Status_SteamDisconnected");

    /// <summary>
    /// Whether to show the first-run disclaimer banner.
    /// Dismissed by the user via <see cref="DismissDisclaimer"/>.
    /// </summary>
    [ObservableProperty]
    private bool _showDisclaimer = true;

    /// <summary>Navigate to the Tasks view.</summary>
    [RelayCommand]
    private void NavigateToTasks()
    {
        _navigationService.NavigateTo<TasksViewModel>();
    }

    /// <summary>Navigate to the My File Groups view.</summary>
    [RelayCommand]
    private void NavigateToMyFileGroups()
    {
        _navigationService.NavigateTo<MyFileGroupsViewModel>();
    }

    /// <summary>Navigate to the Downloaded view.</summary>
    [RelayCommand]
    private void NavigateToDownloaded()
    {
        _navigationService.NavigateTo<DownloadedViewModel>();
    }

    /// <summary>Navigate to the Settings view.</summary>
    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.NavigateTo<SettingsViewModel>();
    }

    /// <summary>Navigate to the About view.</summary>
    [RelayCommand]
    private void NavigateToAbout()
    {
        _navigationService.NavigateTo<AboutViewModel>();
    }

    /// <summary>Dismiss the first-run disclaimer banner.</summary>
    [RelayCommand]
    private void DismissDisclaimer()
    {
        ShowDisclaimer = false;
    }
}
