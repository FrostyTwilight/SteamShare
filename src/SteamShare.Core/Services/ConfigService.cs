using System.Text.Json;

using Serilog;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

/// <summary>
/// Manages application configuration persistence in the user data directory.
/// On construction: loads config.json if it exists, falls back to defaults on corruption.
/// Save is atomic (write to .tmp, then rename).
/// </summary>
public sealed class ConfigService : IConfigService
{
    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;

    /// <inheritdoc/>
    public AppConfig Current { get; set; }

    /// <inheritdoc/>
    public string ConfigDirectory { get; }

    public ConfigService(string configDirectory)
    {
        ConfigDirectory = configDirectory;
        _configFilePath = Path.Combine(configDirectory, "config.json");
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        _logger = Log.ForContext<ConfigService>();

        Directory.CreateDirectory(configDirectory);
        _logger.Information("Loading config from {ConfigPath}", _configFilePath);
        Current = LoadFromDisk();
        _logger.Information("Config loaded: language={Language}, theme={Theme}", Current.Language, Current.Theme);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        _logger.Debug("Saving config to {ConfigPath}", _configFilePath);
        var tmpPath = _configFilePath + ".tmp";
        var json = JsonSerializer.Serialize(Current, _jsonOptions);

        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, _configFilePath, overwrite: true);
        _logger.Information("Config saved to {ConfigPath}", _configFilePath);
    }

    /// <inheritdoc/>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        Current = await LoadFromDiskAsync(ct);
    }

    /// <inheritdoc/>
    public string GetStoragePath(string relativePath)
    {
        return Path.Combine(ConfigDirectory, relativePath);
    }

    private AppConfig LoadFromDisk()
    {
        if (!File.Exists(_configFilePath))
        {
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(_configFilePath);
            return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is not ConfigLoadException)
        {
            _logger.Warning(ex, "Failed to load config from {ConfigPath}, falling back to defaults", _configFilePath);
            return new AppConfig();
        }
    }

    private async Task<AppConfig> LoadFromDiskAsync(CancellationToken ct)
    {
        if (!File.Exists(_configFilePath))
        {
            return new AppConfig();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath, ct);
            return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new ConfigLoadException("Failed to reload configuration from disk", ex);
        }
    }
}
