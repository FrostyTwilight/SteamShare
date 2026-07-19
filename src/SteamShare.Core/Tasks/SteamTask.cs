namespace SteamShare.Core.Tasks;

/// <summary>
/// Represents an asynchronous operation in the SteamShare task system.
/// Supports hierarchical task trees with aggregated progress tracking.
/// This is a mutable class because <see cref="Progress"/> and <see cref="Status"/>
/// change over the lifetime of the task.
/// </summary>
public class SteamTask
{
    /// <summary>
    /// Unique identifier for this task, generated as a lowercase GUID without hyphens.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The ID of the parent task, if this task is a child in a task tree.
    /// Null for root-level tasks.
    /// </summary>
    public string? ParentTaskId { get; set; }

    /// <summary>
    /// The category of operation this task performs.
    /// </summary>
    public TaskCategory Category { get; set; }

    /// <summary>
    /// The current lifecycle status of this task. Defaults to <see cref="TaskStatus.Pending"/>.
    /// </summary>
    public TaskStatus Status { get; set; } = TaskStatus.Pending;

    /// <summary>
    /// Progress percentage from 0 to 100.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Human-readable description of the current progress (e.g., "3 of 10 files").
    /// Null when no progress text is available.
    /// </summary>
    public string? ProgressText { get; set; }

    /// <summary>
    /// Human-readable description of what this task does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this task was created.
    /// </summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when this task reached a terminal state
    /// (<see cref="TaskStatus.Completed"/>, <see cref="TaskStatus.Failed"/>, or <see cref="TaskStatus.Cancelled"/>).
    /// Null while the task is <see cref="TaskStatus.Pending"/> or <see cref="TaskStatus.Running"/>.
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// The exception message from the most recent failure, if any.
    /// Null when the task has not failed.
    /// </summary>
    public string? LastException { get; set; }

    /// <summary>
    /// Arbitrary key-value metadata associated with this task.
    /// Useful for storing contextual information like file paths, Steam IDs, or error codes.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// Whether this task should be displayed in the UI.
    /// Set to false for internal or automated subtasks.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Child tasks that make up this task's overall operation.
    /// Progress is aggregated from children when present.
    /// </summary>
    public List<SteamTask> Children { get; init; } = [];

    /// <summary>
    /// Adds a child task to this task's tree.
    /// Sets the child's <see cref="ParentTaskId"/> to this task's <see cref="Id"/>.
    /// </summary>
    /// <param name="child">The child task to add. Must not be null.</param>
    public void AddChild(SteamTask child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.ParentTaskId = Id;
        Children.Add(child);
    }

    /// <summary>
    /// Computes the aggregate progress of this task.
    /// When children are present, returns the average of all children's
    /// <see cref="Progress"/> values. Otherwise, returns this task's own
    /// <see cref="Progress"/>.
    /// </summary>
    /// <returns>A progress value between 0 and 100.</returns>
    public double AggregateProgress()
    {
        if (Children.Count == 0)
        {
            return Progress;
        }

        double sum = 0;
        foreach (var child in Children)
        {
            sum += child.Progress;
        }

        return sum / Children.Count;
    }
}
