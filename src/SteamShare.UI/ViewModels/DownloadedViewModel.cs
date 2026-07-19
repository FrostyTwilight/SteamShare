using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using SteamShare.Core.Localization;
using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.UI.Services;

namespace SteamShare.UI.ViewModels;

/// <summary>
/// ViewModel for the Downloaded Groups section.
/// Lists all locally downloaded file groups from the tracking database.
/// </summary>
public partial class DownloadedViewModel : ViewModelBase
{
    private static readonly ILogger LogSerilog = Log.ForContext<DownloadedViewModel>();
    private readonly TrackingDatabaseService _trackingDb;
    private readonly FileGroupManager _fileGroupManager;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly LocalizationService _loc;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Locally downloaded file groups.</summary>
    public ObservableCollection<FileGroupTrackingEntry> DownloadedGroups { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    public DownloadedViewModel(
        TrackingDatabaseService trackingDb,
        FileGroupManager fileGroupManager,
        INotificationService notificationService,
        IDialogService dialogService,
        LocalizationService loc)
    {
        _trackingDb = trackingDb;
        _fileGroupManager = fileGroupManager;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _loc = loc;
        LogSerilog.Debug("DownloadedViewModel created");
        _ = RunAutoRefreshAsync();
    }

    /// <summary>Refreshes the list from the tracking database.</summary>
    [RelayCommand]
    private void Refresh()
    {
        var entries = _trackingDb.GetByState(DownloadState.Downloaded);
        DownloadedGroups.Clear();
        foreach (var entry in entries)
        {
            DownloadedGroups.Add(entry);
        }
    }

    /// <summary>
    /// Verifies the SHA-256 hash of a downloaded file group.
    /// </summary>
    [RelayCommand]
    private async Task VerifyAsync(FileGroupTrackingEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.LocalPath))
        {
            return;
        }

        try
        {
            IsLoading = true;

            LogSerilog.Information("Verifying integrity of {Name} ({PublishedFileId})",
                entry.CachedName, entry.PublishedFileId);

            var metadata = await _fileGroupManager.GetMetadataAsync(entry.PublishedFileId);
            var fileGroup = FileGroup.FromMetadata(
                metadata.ToMetadata(),
                entry.PublishedFileId,
                metadata.OwnerSteamId,
                entry.CachedVisibility,
                entry.LocalPath);

            var isValid = await _fileGroupManager.VerifyIntegrityAsync(fileGroup);
            if (isValid)
            {
                LogSerilog.Information("Integrity check passed for {Name}", entry.CachedName);
                _notificationService.ShowInfo(_loc.GetString("Notification_VerifyPassed", entry.CachedName));
            }
            else
            {
                LogSerilog.Warning("Integrity check FAILED for {Name}", entry.CachedName);
                _notificationService.ShowError(
                    _loc.GetString("Notification_VerifyFailed", entry.CachedName));
            }
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Integrity check error for {Name}: {Message}",
                entry.CachedName, ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_VerifyError", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Deletes the local downloaded file and removes from tracking.
    /// </summary>
    [RelayCommand]
    private async Task DeleteLocalAsync(FileGroupTrackingEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            _loc.GetString("Title_DeleteLocalFile"),
            _loc.GetString("Prompt_DeleteLocalConfirmation", entry.CachedName));

        if (!confirmed)
        {
            return;
        }

        try
        {
            LogSerilog.Information("Deleting local folder for {Name} ({PublishedFileId})",
                entry.CachedName, entry.PublishedFileId);
            if (!string.IsNullOrEmpty(entry.LocalPath) && Directory.Exists(entry.LocalPath))
            {
                Directory.Delete(entry.LocalPath, recursive: true);
            }

            _trackingDb.Remove(entry.PublishedFileId);
            await _trackingDb.SaveAsync();

            DownloadedGroups.Remove(entry);
            LogSerilog.Information("Local file deleted: {Name}", entry.CachedName);
            _notificationService.ShowInfo(_loc.GetString("Notification_LocalDeleted", entry.CachedName));
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Local delete failed for {Name}: {Message}",
                entry.CachedName, ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_DeleteFailed", ex.Message));
        }
    }

    /// <summary>Opens the folder containing the downloaded file.</summary>
    [RelayCommand]
    private void OpenFolder(FileGroupTrackingEntry? entry)
    {
        if (entry?.LocalPath is not null)
        {
            _dialogService.OpenFolder(entry.LocalPath);
        }
    }

    private async Task RunAutoRefreshAsync()
    {
        await Task.Delay(500).ConfigureAwait(true);
        Refresh();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    Refresh();
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
    }

    public override void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        base.Dispose();
    }
}
