using Avalonia.Controls;

namespace SteamShare.UI.Views;

public partial class ErrorDialog : Window
{
    public string ErrorMessage { get; set; } = string.Empty;

    public ErrorDialog()
    {
        DataContext = this;
        InitializeComponent();
    }

    public ErrorDialog(string message) : this()
    {
        ErrorMessage = message;
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
