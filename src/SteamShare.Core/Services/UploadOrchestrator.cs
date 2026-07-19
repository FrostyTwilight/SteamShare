using System.Diagnostics;

using Serilog;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;
using SteamShare.Core.Tasks;

namespace SteamShare.Core.Services;

/// <summary>
/// Orchestrates the full upload lifecycle: workspace item creation, content
/// upload with blended progress, persistent task tracking for auto-restart
/// across application restarts, and <see cref="ITaskService"/> integration
/// for UI visibility.
/// </summary>
public sealed class UploadOrchestrator
{
    private const int TaskTypeUpload = 1;

    private static readonly ILogger LogSerilog = Log.ForContext<UploadOrchestrator>();

    private readonly IWorkshopService _workshop;
    private readonly FileGroupManager _fileGroupManager;
    private readonly TrackingDatabaseService _trackingDb;
    private readonly ITaskService _taskService;

    public UploadOrchestrator(
        IWorkshopService workshop,
        FileGroupManager fileGroupManager,
        TrackingDatabaseService trackingDb,
        ITaskService taskService)
    {
        _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
        _fileGroupManager = fileGroupManager ?? throw new ArgumentNullException(nameof(fileGroupManager));
        _trackingDb = trackingDb ?? throw new ArgumentNullException(nameof(trackingDb));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
    }

    /// <summary>
    /// Starts an upload from a local directory, creating a Steam Workshop
    /// item and then uploading its content.
    /// </summary>
    /// <param name="sourcePath">Path to the source directory to publish.</param>
    /// <param name="name">
    /// Human-readable name for the file group. When null, extracted from
    /// <c>Path.GetFileName(sourcePath)</c>.
    /// </param>
    /// <param name="virtualFolderPath">Virtual folder path within the file group tree. Empty for root.</param>
    /// <param name="visibility">Workshop visibility for the new item.</param>
    /// <param name="progressCallback">Optional callback for upload progress (bytes so far, total bytes).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the upload finishes or is cancelled.</returns>
    public async Task StartUploadAsync(
        string sourcePath,
        string? name,
        string virtualFolderPath = "",
        WorkshopVisibility visibility = WorkshopVisibility.Private,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");
        }

        // Derive name from the directory when not explicitly provided.
        name ??= Path.GetFileName(sourcePath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
        {
            name = new DirectoryInfo(sourcePath).Name;
        }

        LogSerilog.Information("Starting upload for {Name} from {SourcePath}", name, sourcePath);

        // Wrap the upload in a task scope.
        using var taskScope = _taskService.StartTask(
            category: TaskCategory.Upload,
            description: $"Upload: {name}",
            isVisible: true,
            ct: ct);

        // Track the row for later cleanup.
        var pendingTasks = _trackingDb.GetPendingTasks();
        var pending = pendingTasks.FindLast(p =>
            p.TaskType == TaskTypeUpload &&
            p.SourcePath == sourcePath &&
            p.Name == name);

        if (pending == null)
        {
            // Persist pending task BEFORE starting the actual operation.
            _trackingDb.SavePendingTask(
                taskType: TaskTypeUpload,
                publishedFileId: null,
                shareKey: null,
                password: null,
                sourcePath: sourcePath,
                targetPath: null,
                name: name,
                virtualFolderPath: virtualFolderPath,
                visibility: (int)visibility,
                taskId: taskScope.TaskId);
            pending = _trackingDb.GetPendingTasks().FindLast(p =>
                p.TaskType == TaskTypeUpload &&
                p.SourcePath == sourcePath &&
                p.Name == name);
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
            // Phase 1: Create the workshop item (0 % - 30 %).
            _taskService.ReportProgress(1, "Creating workshop item...");
            var fileGroup = await _fileGroupManager.CreateFromDirectoryAsync(
                sourcePath, name, virtualFolderPath);

            _taskService.ReportProgress(30, "Uploading to Steam...");

            // Phase 2: Upload content (30 % - 100 % blended).
            var lastReportTime = Stopwatch.GetTimestamp();

            await _fileGroupManager.UploadAsync(
                fileGroup,
                visibility: visibility,
                progressCallback: (current, total) =>
                {
                    // Blend progress: creation occupies 0-30, upload
                    // occupies 30-100 of the overall operation.
                    var pct = total > 0
                        ? 30.0 + (double)current / total * 70.0
                        : 30.0;

                    var statusText = total > 0
                        ? $"Uploading... {current:N0} / {total:N0} bytes"
                        : $"Uploading... {current:N0} bytes";

                    _taskService.ReportProgress(pct, statusText);

                    // Throttled external callback.
                    var elapsed = Stopwatch.GetElapsedTime(lastReportTime);
                    if (elapsed.TotalMilliseconds >= 500)
                    {
                        lastReportTime = Stopwatch.GetTimestamp();
                        progressCallback?.Invoke(current, total);
                    }
                },
                ct: ct);

            // Success: remove the pending task row.
            if (pending is not null)
            {
                _trackingDb.DeletePendingTask(pending.Id);
            }

            LogSerilog.Information("Upload completed: {Name} ({PublishedFileId})",
                fileGroup.Name, fileGroup.PublishedFileId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _taskService.Fail(ex);
            LogSerilog.Error(ex, "Upload failed: {Name} — pending task preserved", name);
            throw;
        }
        finally
        {
            if (pending is not null)
            {
                _trackingDb.DeletePendingTask(pending.Id);
            }
        }
        // On cancellation: TaskScope's CT callback sets Status=Cancelled;
        // the pending task is preserved for restart.
    }

    /// <summary>
    /// Returns all pending upload tasks from the tracking database.
    /// </summary>
    public IReadOnlyList<PendingTaskRecord> GetPendingUploads()
    {
        _trackingDb.DeleteDuplicatePendingTasks();
        return _trackingDb.GetPendingTasks()
            .Where(p => p.TaskType == TaskTypeUpload)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Resumes a previously saved pending upload task using the state
    /// captured when it was first created.
    /// </summary>
    /// <param name="task">The pending task record to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RestartPendingUploadAsync(PendingTaskRecord task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.TaskType != TaskTypeUpload)
        {
            throw new ArgumentException(
                $"Pending task type {task.TaskType} is not an upload task.", nameof(task));
        }

        if (string.IsNullOrEmpty(task.SourcePath))
        {
            throw new InvalidOperationException(
                "Pending upload task is missing the source path and cannot be restarted.");
        }

        if (!Directory.Exists(task.SourcePath))
        {
            LogSerilog.Warning(
                "Source directory {SourcePath} for pending upload {Id} no longer exists — deleting pending task",
                task.SourcePath, task.Id);
            _trackingDb.DeletePendingTask(task.Id);
            return;
        }

        var visibility = (WorkshopVisibility)task.Visibility;

        LogSerilog.Information("Restarting pending upload (id={Id}, sourcePath={SourcePath})",
            task.Id, task.SourcePath);

        // If the task has a runtime task ID and it's still running, skip restart.
        if (task.TaskId != null)
        {
            var existing = _taskService.GetTask(task.TaskId);
            if (existing is { Status: SteamShare.Core.Tasks.TaskStatus.Pending or SteamShare.Core.Tasks.TaskStatus.Running })
            {
                LogSerilog.Information(
                    "Pending upload (id={Id}) task {TaskId} is already {Status}, skipping restart",
                    task.Id, task.TaskId, existing.Status);
                return;
            }
        }

        await StartUploadAsync(
            sourcePath: task.SourcePath,
            name: task.Name,
            virtualFolderPath: task.VirtualFolderPath ?? string.Empty,
            visibility: visibility,
            progressCallback: null,
            ct: ct);
    }
}
