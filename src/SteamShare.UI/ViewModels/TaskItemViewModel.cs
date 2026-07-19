using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SteamShare.Core.Tasks;

using TaskStatus = SteamShare.Core.Tasks.TaskStatus;

namespace SteamShare.UI.ViewModels;

/// <summary>
/// UI-binding wrapper around a <see cref="SteamTask"/>.
/// Exposes task properties as observable bindings and provides
/// a cancel command backed by a <see cref="CancellationTokenSource"/>.
/// </summary>
public partial class TaskItemViewModel : ObservableObject
{
    /// <summary>Unique task identifier (lowercase GUID without hyphens).</summary>
    public string Id { get; }

    /// <summary>Human-readable description of the task.</summary>
    public string Description { get; }

    /// <summary>The operation category (Upload, Download, Share, etc.).</summary>
    public TaskCategory Category { get; }

    /// <summary>Current lifecycle status of the task.</summary>
    [ObservableProperty]
    private TaskStatus _status;

    /// <summary>Progress value from 0 to 100.</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>Human-readable progress text (e.g., "3 of 10 files").</summary>
    [ObservableProperty]
    private string? _progressText;

    /// <summary>UTC timestamp when the task was created.</summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>UTC timestamp when the task reached a terminal state, or null.</summary>
    [ObservableProperty]
    private DateTimeOffset? _endTime;

    /// <summary>True when the task has a recorded exception (i.e., it has failed).</summary>
    public bool HasError => LastException != null;

    /// <summary>Exception message from the most recent failure, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _lastException;

    /// <summary>Child tasks shown in a nested tree.</summary>
    public ObservableCollection<TaskItemViewModel> Children { get; } = [];

    /// <summary>Whether this task node is expanded in the UI tree.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Cancellation token source bound to this task's underlying operation.
    /// Set by the owner ViewModel before the task starts executing.
    /// </summary>
    public CancellationTokenSource? Cts { get; set; }

    /// <summary>
    /// Creates a UI ViewModel from a <see cref="SteamTask"/>,
    /// recursively wrapping all child tasks.
    /// </summary>
    public TaskItemViewModel(SteamTask task)
    {
        Id = task.Id;
        Description = task.Description;
        Category = task.Category;
        _status = task.Status;
        _progress = task.Progress;
        _progressText = task.ProgressText;
        StartTime = task.StartTime;
        _endTime = task.EndTime;
        _lastException = task.LastException;

        foreach (var child in task.Children)
        {
            Children.Add(new TaskItemViewModel(child));
        }
    }

    /// <summary>
    /// Cancels the underlying operation by triggering the stored
    /// <see cref="CancellationTokenSource"/>.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Cts?.Cancel();
    }
}
