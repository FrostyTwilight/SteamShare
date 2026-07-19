using System.Threading;

namespace SteamShare.Core.Tasks;

/// <summary>
/// A disposable scope that manages the lifecycle of a <see cref="SteamTask"/>
/// and exposes its unique identifier. Disposing the scope automatically
/// completes the task if it is still in a non-terminal state.
/// </summary>
public interface ITaskScope : IDisposable
{
    /// <summary>
    /// The unique identifier of the task managed by this scope.
    /// </summary>
    string TaskId { get; }
}

/// <summary>
/// Service for managing asynchronous tasks with progress tracking
/// via ambient <see cref="TaskContext"/> (AsyncLocal) context.
/// Tasks wrap operations such as uploads and downloads, providing
/// hierarchical task trees with aggregated progress.
/// </summary>
public interface ITaskService
{
    /// <summary>
    /// Starts a new task as a child of <see cref="TaskContext.Current"/>
    /// if one exists, or as a root task otherwise.
    /// When the provided <paramref name="ct"/> is cancelled, the task
    /// transitions to <see cref="TaskStatus.Cancelled"/>.
    /// </summary>
    /// <param name="category">The operation category for the task.</param>
    /// <param name="description">Human-readable description of what the task does.</param>
    /// <param name="isVisible">
    /// Whether the task should appear in visible task lists.
    /// Set to <c>false</c> for internal or automated subtasks.
    /// </param>
    /// <param name="ct">Cancellation token that cancels the task when triggered.</param>
    /// <returns>
    /// An <see cref="ITaskScope"/> that automatically completes the task on disposal
    /// if it is still in a non-terminal state (<see cref="TaskStatus.Pending"/>
    /// or <see cref="TaskStatus.Running"/>).
    /// </returns>
    ITaskScope StartTask(
        TaskCategory category,
        string description,
        bool isVisible = true,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the progress of the ambient task (the current <see cref="TaskContext.Current"/>).
    /// Must be called within an active task scope.
    /// </summary>
    /// <param name="progress">Progress value between 0 and 100.</param>
    /// <param name="statusText">
    /// Optional human-readable status text describing the current step
    /// (e.g., "3 of 10 files", "Uploading...").
    /// </param>
    void ReportProgress(double progress, string? statusText = null);

    /// <summary>
    /// Marks the ambient task as <see cref="TaskStatus.Failed"/> with the given exception.
    /// Sets <see cref="SteamTask.EndTime"/> to the current UTC time and
    /// <see cref="SteamTask.LastException"/> to <paramref name="ex"/>'s string representation.
    /// </summary>
    /// <param name="ex">The exception that caused the failure.</param>
    void Fail(Exception ex);

    /// <summary>
    /// Marks the ambient task as <see cref="TaskStatus.Completed"/>.
    /// Sets <see cref="SteamTask.EndTime"/> to the current UTC time
    /// and <see cref="SteamTask.Progress"/> to 100.
    /// </summary>
    void Complete();

    /// <summary>
    /// Returns all root-level tasks (tasks with no parent) where
    /// <see cref="SteamTask.IsVisible"/> is <c>true</c>.
    /// </summary>
    /// <returns>A read-only snapshot of the current visible root tasks.</returns>
    IReadOnlyList<SteamTask> GetVisibleRootTasks();

    /// <summary>
    /// Finds a task by its unique identifier, searching the entire task tree.
    /// </summary>
    /// <param name="taskId">The task ID to look up.</param>
    /// <returns>The matching <see cref="SteamTask"/>, or <c>null</c> if not found.</returns>
    SteamTask? GetTask(string taskId);

    /// <summary>
    /// Fired whenever a task's <see cref="SteamTask.Status"/> or
    /// <see cref="SteamTask.Progress"/> changes.
    /// Subscribers can use this to update UI elements or trigger side effects.
    /// </summary>
    event Action<SteamTask>? OnTaskChanged;
}

/// <summary>
/// Internal disposable scope that manages the lifecycle of a <see cref="SteamTask"/>.
/// Registers a callback on the provided <see cref="CancellationToken"/> to cancel the task,
/// and automatically completes the task on disposal if it is still in a non-terminal state.
/// </summary>
/// <remarks>
/// Created by <see cref="ITaskService.StartTask"/> and returned as <see cref="IDisposable"/>.
/// The caller wraps task execution in a <c>using</c> block; when the block exits,
/// the task is automatically completed (or left in its terminal state if already
/// failed or cancelled).
/// </remarks>
internal sealed class TaskScope : IDisposable
{
    private readonly SteamTask _task;
    private readonly CancellationTokenRegistration _ctr;
    private readonly Action<SteamTask>? _onChanged;

    /// <summary>
    /// Creates a new task scope for the given task.
    /// </summary>
    /// <param name="task">The task whose lifecycle this scope manages.</param>
    /// <param name="ct">
    /// Cancellation token whose cancellation transitions the task to
    /// <see cref="TaskStatus.Cancelled"/>.
    /// </param>
    /// <param name="onChanged">
    /// Optional callback invoked whenever the task changes state
    /// (used by the service implementation to fire <see cref="ITaskService.OnTaskChanged"/>).
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="task"/> is null.</exception>
    public TaskScope(SteamTask task, CancellationToken ct, Action<SteamTask>? onChanged = null)
    {
        _task = task ?? throw new ArgumentNullException(nameof(task));
        _onChanged = onChanged;

        if (ct.CanBeCanceled)
        {
            _ctr = ct.Register(() =>
            {
                _task.Status = TaskStatus.Cancelled;
                _task.EndTime ??= DateTimeOffset.UtcNow;
                _onChanged?.Invoke(_task);
            });
        }
    }

    /// <summary>
    /// Disposes the <see cref="CancellationTokenRegistration"/> and,
    /// if the task is still <see cref="TaskStatus.Pending"/> or
    /// <see cref="TaskStatus.Running"/>, completes it automatically
    /// by setting <see cref="SteamTask.Status"/> to <see cref="TaskStatus.Completed"/>,
    /// <see cref="SteamTask.EndTime"/> to the current UTC time, and
    /// <see cref="SteamTask.Progress"/> to 100.
    /// </summary>
    public void Dispose()
    {
        _ctr.Dispose();

        if (_task.Status is TaskStatus.Pending or TaskStatus.Running)
        {
            _task.Status = TaskStatus.Completed;
            _task.EndTime = DateTimeOffset.UtcNow;
            _task.Progress = 100;
            _onChanged?.Invoke(_task);
        }
    }
}
