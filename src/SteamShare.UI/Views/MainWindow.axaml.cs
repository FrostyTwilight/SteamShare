using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;

using SteamShare.UI.Services;

namespace SteamShare.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Wire up the toast notification overlay
        var notificationService = AppServices.Provider.GetRequiredService<INotificationService>();
        ToastContainer.ItemsSource = ((NotificationService)notificationService).Toasts;
    }
}
