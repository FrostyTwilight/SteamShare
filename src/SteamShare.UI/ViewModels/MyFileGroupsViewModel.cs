using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using SteamShare.Core.Localization;
using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.Core.Tasks;
using SteamShare.UI.Services;
using SteamShare.UI.Views;

namespace SteamShare.UI.ViewModels;

/// <summary>
/// ViewModel for the My File Groups section.
/// Lists owned file groups with upload, share, rename, delete, and visibility actions.
/// </summary>
public partial class MyFileGroupsViewModel : ViewModelBase
{
    private static readonly ILogger LogSerilog = Log.ForContext<MyFileGroupsViewModel>();
    private readonly FileGroupManager _fileGroupManager;
    private readonly WorkshopQueryService _workshopQuery;
    private readonly ShareService _shareService;
    private readonly VisibilityService _visibilityService;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly LocalizationService _loc;
    private readonly IWorkshopService _workshop;
    private readonly ITaskService _taskService;
    private readonly UploadOrchestrator _uploadOrchestrator;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Owned file groups from the Steam Workshop.</summary>
    public ObservableCollection<FileGroup> OwnedGroups { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    public MyFileGroupsViewModel(
        FileGroupManager fileGroupManager,
        WorkshopQueryService workshopQuery,
        ShareService shareService,
        VisibilityService visibilityService,
        INotificationService notificationService,
        IDialogService dialogService,
        LocalizationService loc,
        IWorkshopService workshop,
        ITaskService taskService,
        UploadOrchestrator uploadOrchestrator)
    {
        _fileGroupManager = fileGroupManager;
        _workshopQuery = workshopQuery;
        _shareService = shareService;
        _visibilityService = visibilityService;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _loc = loc;
        _workshop = workshop;
        _taskService = taskService;
        _uploadOrchestrator = uploadOrchestrator;
        LogSerilog.Debug("MyFileGroupsViewModel created");
        _ = RunAutoRefreshAsync();

        // Auto-restart pending uploads moved to App.RestartPendingTasksAsync (at UI startup)
        // _ = RestartPendingUploadsOnLaunchAsync();
    }

    /// <summary>Refreshes the list of owned file groups from the workshop.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await DoRefreshAsync(showLoading: true);
    }

    /// <summary>Lightweight refresh without showing the loading indicator. Used by auto-refresh timer.</summary>
    private async Task RefreshSilentAsync()
    {
        await DoRefreshAsync(showLoading: false);
    }

    private async Task DoRefreshAsync(bool showLoading)
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (showLoading)
            {
                IsLoading = true;
            }
            var groups = await _workshopQuery.QueryOwnedFileGroupsAsync();
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            OwnedGroups.Clear();
            foreach (var group in groups)
            {
                OwnedGroups.Add(group);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Refresh failed: {Message}", ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_RefreshFailed", ex.Message));
        }
        finally
        {
            if (showLoading)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// Opens a folder picker, then delegates the upload to
    /// <see cref="UploadOrchestrator.StartUploadAsync"/>.
    /// </summary>
    [RelayCommand]
    private async Task UploadAsync()
    {
        try
        {
            var folderPath = await _dialogService.PickFolderAsync();
            if (folderPath is null)
            {
                return;
            }

            var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name))
            {
                name = new DirectoryInfo(folderPath).Name;
            }

            LogSerilog.Information("Starting upload for {Name} from {Path}", name, folderPath);
            _notificationService.ShowInfo(_loc.GetString("Notification_UploadStarted", name));

            await _uploadOrchestrator.StartUploadAsync(folderPath, name: null);

            LogSerilog.Information("Upload completed: {Name}", name);
            _notificationService.ShowInfo(_loc.GetString("Notification_Uploaded", name));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Upload failed: {Message}", ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_UploadFailed", ex.Message));
        }
    }

    /// <summary>
    /// Auto-restarts pending uploads that were saved in the tracking database.
    /// Runs as fire-and-forget on a background thread to avoid blocking the UI.
    /// </summary>
    private async Task RestartPendingUploadsOnLaunchAsync()
    {
        try
        {
            var pending = _uploadOrchestrator.GetPendingUploads();
            foreach (var task in pending)
            {
                try
                {
                    LogSerilog.Information("Restarting pending upload (id={Id})", task.Id);
                    await _uploadOrchestrator.RestartPendingUploadAsync(task);
                }
                catch (Exception ex)
                {
                    LogSerilog.Error(ex, "Failed to restart pending upload (id={Id})", task.Id);
                }
            }
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Failed to get pending uploads for auto-restart");
        }
    }

    /// <summary>Opens a share dialog to generate a share key for the specified file group.</summary>
    [RelayCommand]
    private async Task ShareAsync(FileGroup? group)
    {
        if (group is null)
        {
            return;
        }

        try
        {
            var mainWindow = App.Current?.GetMainWindow();
            if (mainWindow is null)
            {
                return;
            }

            var dialog = new ShareDialog(_shareService, group);
            await dialog.ShowDialog<ShareDialog>(mainWindow);
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(_loc.GetString("Notification_ShareKeyGenerateFailed", ex.Message));
        }
    }

    /// <summary>Deletes a file group from the workshop after confirmation.</summary>
    [RelayCommand]
    private async Task DeleteAsync(FileGroup? group)
    {
        if (group is null)
        {
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            _loc.GetString("Title_DeleteFileGroup"),
            _loc.GetString("Prompt_DeleteFileGroupConfirmation", group.Name));

        if (!confirmed)
        {
            return;
        }

        try
        {
            LogSerilog.Information("Deleting file group {Name} ({PublishedFileId})",
                group.Name, group.PublishedFileId);
            await _fileGroupManager.DeleteAsync(group.PublishedFileId);
            OwnedGroups.Remove(group);
            LogSerilog.Information("File group deleted: {Name}", group.Name);
            _notificationService.ShowInfo(_loc.GetString("Notification_Deleted", group.Name));
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Delete failed for {Name}: {Message}", group.Name, ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_DeleteFailed", ex.Message));
        }
    }

    /// <summary>Renames a file group after prompting for the new name.</summary>
    [RelayCommand]
    private async Task RenameAsync(FileGroup? group)
    {
        if (group is null)
        {
            return;
        }

        var newName = await _dialogService.PromptAsync(_loc.GetString("Title_Rename"), _loc.GetString("Prompt_EnterNewName"), group.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == group.Name)
        {
            return;
        }

        // Validate: reject names containing invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalidChars) >= 0 || newName.Contains('/') || newName.Contains('\\'))
        {
            _notificationService.ShowError(_loc.GetString("Notification_InvalidName"));
            return;
        }

        try
        {
            await _fileGroupManager.RenameAsync(group.PublishedFileId, newName);
            await RefreshAsync();
            _notificationService.ShowInfo(_loc.GetString("Notification_Renamed", newName));
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Rename failed: {Message}", ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_RenameFailed", ex.Message));
        }
    }

    /// <summary>Changes the visibility of a file group after confirmation.</summary>
    [RelayCommand]
    private async Task ChangeVisibilityAsync(FileGroup? group)
    {
        if (group is null)
        {
            return;
        }

        var targetVisibility = group.Visibility switch
        {
            WorkshopVisibility.Private => WorkshopVisibility.Unlisted,
            WorkshopVisibility.Unlisted => WorkshopVisibility.Public,
            WorkshopVisibility.Public => WorkshopVisibility.Private,
            _ => WorkshopVisibility.Private,
        };

        var warning = _visibilityService.GetVisibilityWarning(group.Visibility, targetVisibility);
        if (!string.IsNullOrEmpty(warning))
        {
            var confirmed = await _dialogService.ConfirmAsync(
                _loc.GetString("Title_ChangeVisibility"),
                _loc.GetString("Prompt_VisibilityPublicConfirm", group.Name));

            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            await _visibilityService.ChangeVisibilityAsync(
                group.PublishedFileId,
                targetVisibility,
                confirmed: targetVisibility == WorkshopVisibility.Public);

            await RefreshAsync();
            _notificationService.ShowInfo(_loc.GetString("Notification_VisibilityChanged", group.Name));
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Visibility change failed: {Message}", ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_VisibilityChangeFailed", ex.Message));
        }
    }

    private async Task RunAutoRefreshAsync()
    {
        try
        {
            await RefreshSilentAsync();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    await RefreshSilentAsync();
                }
                catch (OperationCanceledException) { break; }
                catch { /* silent — individual refresh failures shouldn't kill the timer */ }
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
