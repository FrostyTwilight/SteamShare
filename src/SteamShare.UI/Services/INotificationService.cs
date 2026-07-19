namespace SteamShare.UI.Services;

/// <summary>
/// Service for showing toast notifications in the UI.
/// </summary>
public interface INotificationService
{
    /// <summary>Shows an error toast notification.</summary>
    void ShowError(string message);

    /// <summary>Shows a warning toast notification.</summary>
    void ShowWarning(string message);

    /// <summary>Shows an informational toast notification.</summary>
    void ShowInfo(string message);
}
