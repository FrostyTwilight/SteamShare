using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using Serilog;

using SteamShare.Core.Localization;

namespace SteamShare.UI.Services;

/// <summary>
/// Platform dialog service using Avalonia's TopLevel.StorageProvider
/// for file/folder pickers and OS file explorer for folder opening.
/// Confirm/Prompt dialogs use lightweight modal windows.
/// </summary>
public sealed class DialogService : IDialogService
{
    private static readonly ILogger LogSerilog = Log.ForContext<DialogService>();
    private readonly LocalizationService _loc;

    public DialogService(LocalizationService loc)
    {
        _loc = loc;
    }

    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<string?> PickFolderAsync()
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var topLevel = TopLevel.GetTopLevel(window);
        if (topLevel is null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    /// <inheritdoc/>
    public async Task<string?> PickFileAsync()
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var topLevel = TopLevel.GetTopLevel(window);
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { AllowMultiple = false });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    /// <inheritdoc/>
    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start("explorer.exe", $"\"{path}\"");
        }
        else if (File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        LogSerilog.Debug("Showing confirm dialog: {Title}", title);
        var window = GetMainWindow();
        if (window is null)
        {
            LogSerilog.Warning("Confirm dialog skipped: no main window");
            return false;
        }

        var tcs = new TaskCompletionSource<bool>();

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        var noButton = new Button
        {
            Content = _loc.GetString("Label_No"),
            Width = 80,
        };
        noButton.Click += (_, _) =>
        {
            tcs.TrySetResult(false);
            dialog.Close();
        };

        var yesButton = new Button
        {
            Content = _loc.GetString("Label_Yes"),
            Width = 80,
        };
        yesButton.Click += (_, _) =>
        {
            tcs.TrySetResult(true);
            dialog.Close();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { noButton, yesButton },
                },
            },
        };

        await dialog.ShowDialog<bool>(window);
        var result = await tcs.Task;
        LogSerilog.Debug("Confirm dialog result: {Result} for {Title}", result, title);
        return result;
    }

    /// <inheritdoc/>
    public async Task<string?> PromptAsync(string title, string message, string defaultValue = "")
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var tcs = new TaskCompletionSource<string?>();

        var textBox = new TextBox
        {
            Text = defaultValue,
            PlaceholderText = _loc.GetString("Prompt_EnterValue"),
        };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        var cancelButton = new Button
        {
            Content = _loc.GetString("Label_Cancel"),
            Width = 80,
        };
        cancelButton.Click += (_, _) =>
        {
            tcs.TrySetResult(null);
            dialog.Close();
        };

        var okButton = new Button
        {
            Content = _loc.GetString("Label_OK"),
            Width = 80,
        };
        okButton.Click += (_, _) =>
        {
            tcs.TrySetResult(textBox.Text);
            dialog.Close();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                textBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, okButton },
                },
            },
        };

        await dialog.ShowDialog<bool>(window);
        return await tcs.Task;
    }
}
