namespace SteamShare.UI.Services;

/// <summary>
/// Toast notification type.
/// </summary>
public enum ToastType
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Represents a single toast notification message.
/// </summary>
public class ToastMessage
{
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
