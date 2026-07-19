using System.Collections.ObjectModel;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using SteamShare.Core.Localization;
using SteamShare.Core.Services;
using SteamShare.Core.Tasks;
using SteamShare.Core.Utilities;
using SteamShare.UI.Services;

using TaskStatus = SteamShare.Core.Tasks.TaskStatus;

namespace SteamShare.UI.ViewModels;

/// <summary>
/// ViewModel for the Tasks (任务) tab — replaces the old Downloads tab.
/// Shows active and completed tasks via <see cref="ITaskService"/>,
/// and drives the download workflow with integrated progress reporting.
/// </summary>
public partial class TasksViewModel : ViewModelBase
{
    private static readonly ILogger LogSerilog = Log.ForContext<TasksViewModel>();
    private readonly ITaskService _taskService;
    private readonly IShareKeyCryptoService _cryptoService;
    private readonly ShareService _shareService;
    private readonly FileGroupManager _fileGroupManager;
    private readonly IConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly LocalizationService _loc;
    private readonly IWorkshopService _workshop;
    private readonly DownloadOrchestrator _downloadOrchestrator;

    /// <summary>Currently active (pending or running) root tasks.</summary>
    public ObservableCollection<TaskItemViewModel> ActiveTasks { get; } = [];

    /// <summary>Terminal (completed, failed, or cancelled) root tasks.</summary>
    public ObservableCollection<TaskItemViewModel> CompletedTasks { get; } = [];

    /// <summary>True while a download or refresh is in progress.</summary>
    [ObservableProperty]
    private bool _isLoading;

    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private const int ThrottleMs = 250;

    /// <summary>Share key entered by the user for downloading.</summary>
    [ObservableProperty]
    private string _shareKey = string.Empty;

    /// <summary>Optional password for encrypted share keys.</summary>
    [ObservableProperty]
    private string? _password;

    public TasksViewModel(
        ITaskService taskService,
        IShareKeyCryptoService cryptoService,
        ShareService shareService,
        FileGroupManager fileGroupManager,
        IConfigService configService,
        INotificationService notificationService,
        IDialogService dialogService,
        LocalizationService loc,
        IWorkshopService workshop,
        DownloadOrchestrator downloadOrchestrator)
    {
        _taskService = taskService;
        _cryptoService = cryptoService;
        _shareService = shareService;
        _fileGroupManager = fileGroupManager;
        _configService = configService;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _loc = loc;
        _workshop = workshop;
        _downloadOrchestrator = downloadOrchestrator;

        _taskService.OnTaskChanged += OnTaskChanged;
        LogSerilog.Debug("TasksViewModel created");

        Refresh();

        // Auto-restart pending downloads moved to App.RestartPendingTasksAsync (at UI startup)
        // _ = RestartPendingDownloadsOnLaunchAsync();
    }

    /// <summary>
    /// Refreshes the task lists from <see cref="ITaskService.GetVisibleRootTasks"/>,
    /// splitting root tasks into active vs. completed collections.
    /// Dispatches to the UI thread when called from a background thread
    /// (e.g. <see cref="OnTaskChanged"/> via Steam/I/O callbacks).
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshCore();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshCore, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Internal method that performs the actual collection mutation.
    /// Must always execute on the UI thread.
    /// </summary>
    private void RefreshCore()
    {
        var roots = _taskService.GetVisibleRootTasks();

        var activeList = new List<TaskItemViewModel>();
        var completedList = new List<TaskItemViewModel>();

        foreach (var task in roots)
        {
            var vm = new TaskItemViewModel(task);

            if (task.Status is TaskStatus.Pending or TaskStatus.Running)
            {
                activeList.Add(vm);
            }
            else
            {
                completedList.Add(vm);
            }
        }

        ActiveTasks.Clear();
        CompletedTasks.Clear();

        foreach (var item in activeList)
        {
            ActiveTasks.Add(item);
        }

        foreach (var item in completedList)
        {
            CompletedTasks.Add(item);
        }
    }

    /// <summary>
    /// Starts a download from the entered share key, delegating all
    /// business logic to <see cref="DownloadOrchestrator.StartDownloadAsync"/>.
    /// </summary>
    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(ShareKey))
        {
            _notificationService.ShowWarning(_loc.GetString("Prompt_EnterShareKey"));
            return;
        }

        try
        {
            IsLoading = true;

            LogSerilog.Information("Starting download from share key");

            var password = string.IsNullOrWhiteSpace(Password) ? null : Password.Trim();
            await _downloadOrchestrator.StartDownloadAsync(ShareKey.Trim(), password, targetPath: _configService.Current.DownloadDirectory);

            LogSerilog.Information("Download completed");
            _notificationService.ShowInfo(_loc.GetString("Notification_DownloadComplete"));

            // Clear input fields on success
            ShareKey = string.Empty;
            Password = null;
        }
        catch (OperationCanceledException)
        {
            LogSerilog.Information("Download cancelled by user");
            _notificationService.ShowInfo(_loc.GetString("Notification_DownloadCancelled"));
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Download failed: {Message}", ex.Message);
            _notificationService.ShowError(_loc.GetString("Notification_DownloadFailed", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Auto-restarts pending downloads that were saved in the tracking database.
    /// Runs as fire-and-forget on a background thread to avoid blocking the UI.
    /// </summary>
    private async Task RestartPendingDownloadsOnLaunchAsync()
    {
        try
        {
            var pending = _downloadOrchestrator.GetPendingDownloads();
            foreach (var task in pending)
            {
                try
                {
                    LogSerilog.Information("Restarting pending download (id={Id})", task.Id);
                    await _downloadOrchestrator.RestartPendingDownloadAsync(task);
                }
                catch (Exception ex)
                {
                    LogSerilog.Error(ex, "Failed to restart pending download (id={Id})", task.Id);
                }
            }
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Failed to get pending downloads for auto-restart");
        }
    }

    /// <summary>
    /// Handler for <see cref="ITaskService.OnTaskChanged"/>.
    /// Refreshes the task lists so the UI always reflects current state.
    /// </summary>
    private void OnTaskChanged(SteamTask task)
    {
        // Terminal state changes always refresh immediately
        bool isTerminal = task.Status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Cancelled or TaskStatus.Pending;

        if (isTerminal)
        {
            _lastRefresh = DateTimeOffset.UtcNow;
            Refresh();
            return;
        }

        // Throttle progress updates to avoid overwhelming the dispatcher
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastRefresh).TotalMilliseconds < ThrottleMs)
        {
            return;
        }

        _lastRefresh = now;
        Refresh();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _taskService.OnTaskChanged -= OnTaskChanged;
        base.Dispose();
    }
}
