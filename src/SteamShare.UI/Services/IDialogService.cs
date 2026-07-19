namespace SteamShare.UI.Services;

/// <summary>
/// Service for platform-specific dialogs, file/folder pickers, and folder opening.
/// </summary>
public interface IDialogService
{
    /// <summary>Opens a folder picker and returns the selected path, or null.</summary>
    Task<string?> PickFolderAsync();

    /// <summary>Opens a file picker and returns the selected path, or null.</summary>
    Task<string?> PickFileAsync();

    /// <summary>Opens the specified folder in the OS file explorer.</summary>
    void OpenFolder(string path);

    /// <summary>Shows a confirmation dialog and returns the user's choice.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Shows a text prompt dialog and returns the user's input, or null.</summary>
    Task<string?> PromptAsync(string title, string message, string defaultValue = "");
}
