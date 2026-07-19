namespace SteamShare.Core.Models;

/// <summary>
/// Application configuration, persisted as config.json in user data directory.
/// </summary>
public sealed record AppConfig
{
    /// <summary>Schema version for config migration.</summary>
    public int Version { get; init; } = 1;

    /// <summary>UI language code (zh-CN, en-US).</summary>
    public string Language { get; init; } = "en-US";

    /// <summary>UI theme (System, Light, Dark).</summary>
    public string Theme { get; init; } = "System";

    /// <summary>Custom download directory. Null = OS default downloads folder.</summary>
    public string? DownloadDirectory { get; init; }

    /// <summary>Auto-tracker polling interval in seconds. Default 60.</summary>
    public int AutoTrackIntervalSeconds { get; init; } = 60;

    /// <summary>Whether the startup disclaimer has been dismissed.</summary>
    public bool DisclaimerDismissed { get; init; }

    /// <summary>Path to the Steam installation. Null = auto-detect.</summary>
    public string? SteamPath { get; init; }

    /// <summary>Whether to auto-restart pending download/upload tasks on startup. Default true.</summary>
    public bool AutoRestartPendingTasks { get; init; } = true;
}
