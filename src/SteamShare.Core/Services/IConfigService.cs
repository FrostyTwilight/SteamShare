using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

/// <summary>
/// Manages application configuration persistence in the user data directory.
/// </summary>
public interface IConfigService
{
    /// <summary>Current application configuration.</summary>
    AppConfig Current { get; }

    /// <summary>Root directory where config and storage files reside.</summary>
    string ConfigDirectory { get; }

    /// <summary>Persist current configuration to disk atomically.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Re-read configuration from disk.</summary>
    Task ReloadAsync(CancellationToken ct = default);

    /// <summary>Get a full path relative to the config directory.</summary>
    string GetStoragePath(string relativePath);
}
