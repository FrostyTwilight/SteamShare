using System.Collections.ObjectModel;

using Serilog;

namespace SteamShare.UI.Services;

/// <summary>
/// Toast notification service. Toasts appear in the top-right corner
/// and auto-dismiss after 5 seconds.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private static readonly ILogger LogSerilog = Log.ForContext<NotificationService>();
    private const int AutoDismissMs = 5000;

    /// <summary>
    /// Observable collection of active toasts. Bind to this from the UI.
    /// </summary>
    public ObservableCollection<ToastMessage> Toasts { get; } = new();

    /// <inheritdoc/>
    public void ShowError(string message)
    {
        LogSerilog.Warning("Toast error: {Message}", message);
        AddToast(message, ToastType.Error);
    }

    /// <inheritdoc/>
    public void ShowWarning(string message)
    {
        LogSerilog.Warning("Toast warning: {Message}", message);
        AddToast(message, ToastType.Warning);
    }

    /// <inheritdoc/>
    public void ShowInfo(string message)
    {
        LogSerilog.Information("Toast info: {Message}", message);
        AddToast(message, ToastType.Info);
    }

    private void AddToast(string message, ToastType type)
    {
        var toast = new ToastMessage
        {
            Message = message,
            Type = type,
            CreatedAt = DateTime.Now,
        };

        Toasts.Add(toast);

        _ = Task.Delay(AutoDismissMs).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
        });
    }
}
