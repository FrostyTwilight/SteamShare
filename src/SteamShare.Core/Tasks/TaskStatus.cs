namespace SteamShare.Core.Tasks;

/// <summary>
/// Represents the current lifecycle state of a <see cref="SteamTask"/>.
/// </summary>
public enum TaskStatus
{
    /// <summary>Task has been created but not yet started.</summary>
    Pending,

    /// <summary>Task is actively executing.</summary>
    Running,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task terminated with an error.</summary>
    Failed,

    /// <summary>Task was cancelled before completion.</summary>
    Cancelled
}
