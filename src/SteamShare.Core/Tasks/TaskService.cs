using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SteamShare.Core.Tasks;

/// <summary>
/// Production implementation of <see cref="ITaskService"/> that manages
/// the entire task tree using <see cref="ConcurrentDictionary{TKey, TValue}"/>
/// for O(1) task lookup and a locked <see cref="List{T}"/> for root task ordering.
/// </summary>
public class TaskService : ITaskService
{
    private readonly ConcurrentDictionary<string, SteamTask> _allTasks = new();
    private readonly List<SteamTask> _rootTasks = [];
    private readonly object _rootLock = new();
    private readonly ILogger<TaskService> _logger;

    /// <summary>
    /// Creates a new <see cref="TaskService"/> with optional logger injection.
    /// A null-logger is used when no logger is provided.
    /// </summary>
    public TaskService(ILogger<TaskService>? logger = null)
    {
        _logger = logger ?? NullLogger<TaskService>.Instance;
    }

    /// <inheritdoc />
    public event Action<SteamTask>? OnTaskChanged;

    /// <inheritdoc />
    public ITaskScope StartTask(
        TaskCategory category,
        string description,
        bool isVisible = true,
        CancellationToken ct = default)
    {
        var task = new SteamTask
        {
            Category = category,
            Description = description,
            IsVisible = isVisible,
            Status = TaskStatus.Pending,
        };

        // Register in the global task map.
        _allTasks.TryAdd(task.Id, task);

        // Attach to parent or register as root.
        var ambient = TaskContext.Current;
        if (ambient != null)
        {
            ambient.Task.AddChild(task);
        }
        else
        {
            lock (_rootLock)
            {
                _rootTasks.Add(task);
            }
        }

        // Enter the new task as the ambient context.
        var contextScope = TaskContext.Enter(task);

        task.Status = TaskStatus.Running;
        OnTaskChanged?.Invoke(task);

        // Create the TaskScope that handles CT cancellation + auto-complete.
        var taskScope = new TaskScope(task, ct, t => OnTaskChanged?.Invoke(t));

        return new TaskScopeProxy(task, contextScope, taskScope);
    }

    /// <inheritdoc />
    public void ReportProgress(double progress, string? statusText = null)
    {
        var current = TaskContext.Current?.Task;
        if (current == null)
        {
            _logger.LogWarning("ReportProgress called without an active task context.");
            return;
        }

        current.Progress = progress;
        current.ProgressText = statusText;

        // Propagate aggregation up to parent if one exists.
        if (current.ParentTaskId != null
            && _allTasks.TryGetValue(current.ParentTaskId, out var parent))
        {
            parent.Progress = parent.AggregateProgress();
            OnTaskChanged?.Invoke(parent);
        }

        OnTaskChanged?.Invoke(current);
    }

    /// <inheritdoc />
    public void Fail(Exception ex)
    {
        var current = TaskContext.Current?.Task;
        if (current == null)
        {
            _logger.LogWarning("Fail called without an active task context.");
            return;
        }

        current.Status = TaskStatus.Failed;
        current.EndTime = DateTimeOffset.UtcNow;
        current.LastException = ex.ToString();
        OnTaskChanged?.Invoke(current);
    }

    /// <inheritdoc />
    public void Complete()
    {
        var current = TaskContext.Current?.Task;
        if (current == null)
        {
            _logger.LogWarning("Complete called without an active task context.");
            return;
        }

        current.Status = TaskStatus.Completed;
        current.EndTime = DateTimeOffset.UtcNow;
        current.Progress = 100;
        OnTaskChanged?.Invoke(current);
    }

    /// <inheritdoc />
    public IReadOnlyList<SteamTask> GetVisibleRootTasks()
    {
        lock (_rootLock)
        {
            return _rootTasks.Where(t => t.IsVisible).ToList();
        }
    }

    /// <inheritdoc />
    public SteamTask? GetTask(string taskId)
    {
        _allTasks.TryGetValue(taskId, out var task);
        return task;
    }

    /// <summary>
    /// Composite scope returned by <see cref="StartTask"/> that wraps
    /// both the <see cref="TaskContext"/> scope and the CT-registered
    /// <see cref="TaskScope"/>. Disposing this scope disposes both,
    /// ensuring proper context restoration and lifecycle finalisation.
    /// </summary>
    internal sealed class TaskScopeProxy : ITaskScope
    {
        internal readonly SteamTask _task;
        private readonly IDisposable _contextScope;
        private readonly TaskScope _taskScope;

        internal TaskScopeProxy(SteamTask task, IDisposable contextScope, TaskScope taskScope)
        {
            _task = task;
            _contextScope = contextScope;
            _taskScope = taskScope;
        }

        public string TaskId => _task.Id;

        public void Dispose()
        {
            // TaskScope handles CT unregistration and auto-completion
            // if the task is still in a non-terminal state.
            _taskScope.Dispose();

            // Restore the previous TaskContext.
            _contextScope.Dispose();
        }
    }
}
