namespace SteamShare.Core.Utilities;

/// <summary>
/// Provides a single formatting method for human-readable file sizes,
/// eliminating duplication across the codebase.
/// </summary>
public static class FileSizeFormatter
{
    /// <summary>
    /// Formats a byte count into a human-readable string.
    /// </summary>
    /// <param name="bytes">The size in bytes. Negative values are passed through.</param>
    /// <returns>
    /// "0 B" for zero, "123 B" for &lt; 1 KB, "1.2 KB", "3.45 MB", or "1.23 GB".
    /// </returns>
    public static string Format(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B",
        };
    }
}
