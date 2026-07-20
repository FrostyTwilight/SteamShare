using System.Diagnostics;

using Serilog;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;
using SteamShare.Core.Tasks;

namespace SteamShare.Core.Services;

/// <summary>
/// Orchestrates the full download lifecycle: key resolution, persistent task
/// tracking for auto-restart across application restarts, progress throttling,
/// and <see cref="ITaskService"/> integration for UI visibility.
/// </summary>
public sealed class DownloadOrchestrator
{
    private const int TaskTypeDownload = 0;

    private static readonly ILogger LogSerilog = Log.ForContext<DownloadOrchestrator>();

    private readonly IWorkshopService _workshop;
    private readonly FileGroupManager _fileGroupManager;
    private readonly TrackingDatabaseService _trackingDb;
    private readonly ShareService _shareService;
    private readonly ITaskService _taskService;
    private readonly IConfigService _configService;
    private readonly IShareKeyCryptoService _cryptoService;

    public DownloadOrchestrator(
        IWorkshopService workshop,
        FileGroupManager fileGroupManager,
        TrackingDatabaseService trackingDb,
        ShareService shareService,
        ITaskService taskService,
        IConfigService configService,
        IShareKeyCryptoService cryptoService)
    {
        _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
        _fileGroupManager = fileGroupManager ?? throw new ArgumentNullException(nameof(fileGroupManager));
        _trackingDb = trackingDb ?? throw new ArgumentNullException(nameof(trackingDb));
        _shareService = shareService ?? throw new ArgumentNullException(nameof(shareService));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
    }

    /// <summary>
    /// Starts a download from a share key, tracking it as a pending task
    /// so it can be resumed after an application restart.
    /// </summary>
    /// <param name="shareKey">The share key string (sshare+...).</param>
    /// <param name="password">Optional password for encrypted share keys.</param>
    /// <param name="targetPath">
    /// Output directory. When null, auto-generates under the config
    /// directory as <c>downloads/{name}_{guid:N}</c>.
    /// </param>
    /// <param name="progressCallback">Optional callback for download progress (bytes so far, total bytes). Throttled to 500 ms intervals.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the download finishes or is cancelled.</returns>
    public async Task StartDownloadAsync(
        string shareKey,
        string? password,
        string? targetPath,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareKey))
        {
            throw new ArgumentException("Share key must not be null or empty.", nameof(shareKey));
        }

        LogSerilog.Information("Resolving share key for download");

        // Resolve metadata and extract the published file ID.
        var metadata = await _shareService.ResolveShareKeyAsync(shareKey.Trim(), password, ct).ConfigureAwait(false);
        var payload = _cryptoService.ParseShareKey(shareKey.Trim(), password);
        var publishedFileId = payload.Id;
        var name = metadata.Name;

        // Auto-generate target path when none is provided.

        if (string.IsNullOrEmpty(targetPath))
        {
            targetPath = null;
        }
        else
        {
            targetPath = Path.Combine(targetPath, name);
        }

        LogSerilog.Information("Download target: {TargetPath} (id={PublishedFileId})",
            targetPath, publishedFileId);

        // Wrap the download in a task scope so the task service tracks lifecycle.
        using var taskScope = _taskService.StartTask(
            category: TaskCategory.Download,
            description: name,
            isVisible: true,
            ct: ct);

        // Persist pending task with the runtime task ID so restarts can detect duplicates.
        var pendingTasks = _trackingDb.GetPendingTasks();
        var pending = pendingTasks.FindLast(p =>
            p.TaskType == TaskTypeDownload &&
            p.ShareKey == shareKey &&
            p.TargetPath == targetPath);

        if (pending == null)
        {
            _trackingDb.SavePendingTask(
                taskType: TaskTypeDownload,
                publishedFileId: publishedFileId,
                shareKey: shareKey,
                password: password,
                sourcePath: null,
                targetPath: targetPath,
                name: name,
                virtualFolderPath: null,
                visibility: 0,
                taskId: taskScope.TaskId);

            pending = _trackingDb.GetPendingTasks().FindLast(p =>
                p.TaskType == TaskTypeDownload &&
                p.ShareKey == shareKey &&
                p.TargetPath == targetPath);
        }

        Debug.Assert(pending != null);

        if (pending.TaskId != null && pending.TaskId != taskScope.TaskId)
        {
            var existing = _taskService.GetTask(pending.TaskId);
            if (existing is { Status: SteamShare.Core.Tasks.TaskStatus.Pending or SteamShare.Core.Tasks.TaskStatus.Running })
            {
                LogSerilog.Information(
                    "Pending upload (id={Id}) task {TaskId} is already {Status}, skipping restart",
                    pending.Id, pending.TaskId, existing.Status);
                while (existing.Status == Tasks.TaskStatus.Running ||
                    existing.Status == Tasks.TaskStatus.Pending)
                {
                    await Task.Delay(1, ct).ConfigureAwait(false);
                }
                return;
            }
        }

        try
        {
            var lastReportTime = Stopwatch.GetTimestamp();

            // Throttled progress bridge: only invoke the caller's
            // callback every 500 ms.
            void OnProgress(long current, long total)
            {
                var elapsed = Stopwatch.GetElapsedTime(lastReportTime);
                if (elapsed.TotalMilliseconds < 500)
                {
                    return;
                }

                lastReportTime = Stopwatch.GetTimestamp();

                var progress = total > 0 ? (double)current / total * 100.0 : 0;
                _taskService.ReportProgress(progress,
                    $"Downloading... {current:N0} / {(total > 0 ? total.ToString("N0") : "?")} bytes");

                progressCallback?.Invoke(current, total);
            }

            await _fileGroupManager.DownloadAsync(
                publishedFileId,
                targetPath,
                progressCallback: OnProgress,
                ct: ct).ConfigureAwait(false);

            // Success: remove the pending task row.
            if (pending is not null)
            {
                _trackingDb.DeletePendingTask(pending.Id);
            }

            LogSerilog.Information("Download completed: {Name} ({PublishedFileId})",
                name, publishedFileId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _taskService.Fail(ex);
            LogSerilog.Error(ex, "Download failed: {Name} ({PublishedFileId}) — pending task preserved",
                name, publishedFileId);
            throw;
        }
        finally
        {
            if (pending is not null)
            {
                _trackingDb.DeletePendingTask(pending.Id);
            }
        }
        // On cancellation: TaskScope's CT callback already sets
        // Status=Cancelled; the pending task is intentionally kept.
    }

    /// <summary>
    /// Returns all pending download tasks from the tracking database.
    /// </summary>
    public IReadOnlyList<PendingTaskRecord> GetPendingDownloads()
    {
        _trackingDb.DeleteDuplicatePendingTasks();
        return _trackingDb.GetPendingTasks()
            .Where(p => p.TaskType == TaskTypeDownload)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Resumes a previously saved pending download task using the state
    /// captured when it was first created.
    /// </summary>
    /// <param name="task">The pending task record to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RestartPendingDownloadAsync(PendingTaskRecord task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.TaskType != TaskTypeDownload)
        {
            throw new ArgumentException(
                $"Pending task type {task.TaskType} is not a download task.", nameof(task));
        }

        if (string.IsNullOrEmpty(task.ShareKey))
        {
            throw new InvalidOperationException(
                "Pending download task is missing the share key and cannot be restarted.");
        }

        LogSerilog.Information("Restarting pending download (id={Id}, shareKey={ShareKey})",
            task.Id, task.ShareKey);

        // If the task has a runtime task ID and it's still running, skip restart.
        if (task.TaskId != null)
        {
            var existing = _taskService.GetTask(task.TaskId);
            if (existing is { Status: SteamShare.Core.Tasks.TaskStatus.Pending or SteamShare.Core.Tasks.TaskStatus.Running })
            {
                LogSerilog.Information(
                    "Pending download (id={Id}) task {TaskId} is already {Status}, skipping restart",
                    task.Id, task.TaskId, existing.Status);
                return;
            }
        }

        await StartDownloadAsync(
            shareKey: task.ShareKey,
            password: task.Password,
            targetPath: task.TargetPath,
            progressCallback: null,
            ct: ct);
    }
}
