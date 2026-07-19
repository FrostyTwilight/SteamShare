using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

using SteamShare.Core.Localization;
using SteamShare.Core.Models;
using SteamShare.Core.Services;

namespace SteamShare.UI.Views;

public partial class ShareDialog : Window
{
    private readonly LocalizationService _loc;
    private readonly ShareService? _shareService;
    private readonly FileGroup? _fileGroup;
    private string? _shareKey;

    public ShareDialog()
    {
        InitializeComponent();
        _loc = AppServices.Provider.GetRequiredService<LocalizationService>();
    }

    public ShareDialog(ShareService shareService, FileGroup fileGroup) : this()
    {
        _shareService = shareService;
        _fileGroup = fileGroup;

        GroupNameText.Text = fileGroup.Name;

        PasswordCheckBox.IsCheckedChanged += (_, _) =>
        {
            PasswordTextBox.IsVisible = PasswordCheckBox.IsChecked == true;
        };

        ConfirmButton.Click += OnConfirmClick;
        CancelButton.Click += OnCancelClick;
        CopyButton.Click += OnCopyClick;
        CloseButton.Click += OnCloseClick;
    }

    public string? ShareKey => _shareKey;

    private async void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            ConfirmButton.IsEnabled = false;
            ConfirmButton.Content = _loc.GetString("State_Generating");

            var password = PasswordCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(PasswordTextBox.Text)
                ? PasswordTextBox.Text
                : null;

            _shareKey = await _shareService!.GenerateShareKeyAsync(_fileGroup!.PublishedFileId, password);

            // Show result
            InputPanel.IsVisible = false;
            ResultPanel.IsVisible = true;
            KeyTextBox.Text = _shareKey;

            ConfirmButton.IsVisible = false;
            CancelButton.IsVisible = false;
            CloseButton.IsVisible = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Share key generation failed: {Message}", ex.Message);
            KeyTextBox.Text = _loc.GetString("Label_Error") + ": " + ex.Message;
            InputPanel.IsVisible = false;
            ResultPanel.IsVisible = true;

            ConfirmButton.IsVisible = false;
            CancelButton.IsVisible = false;
            CloseButton.IsVisible = true;
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
            ConfirmButton.Content = _loc.GetString("Label_GenerateShareKey");
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _shareKey = null;
        Close();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_shareKey))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_shareKey);
            }
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
