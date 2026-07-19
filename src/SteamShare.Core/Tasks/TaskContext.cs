namespace SteamShare.Core.Tasks;

/// <summary>
/// Ambient task context that flows through async call chains using <see cref="AsyncLocal{T}"/>.
/// Allows any code in the call stack to access the currently active <see cref="SteamTask"/>
/// without explicit parameter passing.
/// </summary>
public sealed class TaskContext
{
    private static readonly AsyncLocal<TaskContext?> _current = new();

    /// <summary>
    /// Gets the current ambient <see cref="TaskContext"/>, or <c>null</c> if no task is active.
    /// </summary>
    public static TaskContext? Current => _current.Value;

    /// <summary>
    /// Gets the <see cref="SteamTask"/> associated with this context.
    /// </summary>
    public SteamTask Task { get; }

    private TaskContext(SteamTask task)
    {
        Task = task;
    }

    /// <summary>
    /// Enters a new task context, making <paramref name="task"/> the current ambient task.
    /// Returns an <see cref="IDisposable"/> that, when disposed, restores the previous context.
    /// </summary>
    /// <param name="task">The task to set as the current ambient task.</param>
    /// <returns>
    /// A scope that, when disposed, restores the previous <see cref="TaskContext"/>.
    /// Use in a <c>using</c> block for automatic cleanup.
    /// </returns>
    public static IDisposable Enter(SteamTask task)
    {
        var previous = _current.Value;
        var context = new TaskContext(task);
        _current.Value = context;
        return new Scope(previous);
    }

    /// <summary>
    /// Private scope that restores the previous ambient task context on disposal.
    /// Supports nested task contexts: entering task B while task A is active and
    /// disposing B's scope restores task A.
    /// </summary>
    private readonly struct Scope : IDisposable
    {
        private readonly TaskContext? _previous;

        public Scope(TaskContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            _current.Value = _previous;
        }
    }
}
