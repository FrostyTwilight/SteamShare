using SteamShare.Core.Models;
using SteamShare.Core.Services;

namespace SteamShare.Test.Services;

public class ConfigServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly ConfigService _service;

    public ConfigServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SteamShareTest_{Guid.NewGuid():N}");
        _service = new ConfigService(_testDir);
    }

    [Fact]
    public void Constructor_CreatesDefaultConfig_WhenNoFileExists()
    {
        _service.Current.Should().NotBeNull();
        _service.Current.Version.Should().Be(1);
        _service.Current.Language.Should().Be("en-US");
        _service.Current.Theme.Should().Be("System");
        _service.Current.AutoTrackIntervalSeconds.Should().Be(60);
        File.Exists(Path.Combine(_testDir, "config.json")).Should().BeFalse(); // not saved until SaveAsync
    }

    [Fact]
    public async Task SaveAsync_CreatesFile_WithCorrectContent()
    {
        _service.Current = _service.Current with { Language = "zh-CN" };
        await _service.SaveAsync();

        string path = Path.Combine(_testDir, "config.json");
        File.Exists(path).Should().BeTrue();

        var reloaded = new ConfigService(_testDir);
        reloaded.Current.Language.Should().Be("zh-CN");
    }

    [Fact]
    public void GetStoragePath_ReturnsPathUnderConfigDir()
    {
        string path = _service.GetStoragePath("test.db");
        path.Should().Be(Path.Combine(_testDir, "test.db"));
    }

    [Fact]
    public void Constructor_HandlesCorruptedConfig_Gracefully()
    {
        File.WriteAllText(Path.Combine(_testDir, "config.json"), "{invalid json!!!");
        var service = new ConfigService(_testDir);
        service.Current.Version.Should().Be(1); // falls back to default
    }

    [Fact]
    public async Task SaveAsync_IsAtomic_DoesNotLeaveTmpFile()
    {
        _service.Current = _service.Current with { Theme = "Dark" };
        await _service.SaveAsync();

        File.Exists(Path.Combine(_testDir, "config.json.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(_testDir, "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task ReloadAsync_ReReadsFromDisk()
    {
        // Save a config to disk
        _service.Current = _service.Current with { Language = "ja-JP" };
        await _service.SaveAsync();

        // Create a second service pointing to same dir, modify disk directly
        var secondService = new ConfigService(_testDir);
        secondService.Current = secondService.Current with { Language = "fr-FR" };
        await secondService.SaveAsync();

        // Reload the first service — should see the updated value
        await _service.ReloadAsync();
        _service.Current.Language.Should().Be("fr-FR");
    }

    [Fact]
    public void ConfigDirectory_ReturnsProvidedPath()
    {
        _service.ConfigDirectory.Should().Be(_testDir);
    }

    // ── Concurrent saves ────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_MultipleSaves_DoesNotCorrupt()
    {
        _service.Current = _service.Current with { AutoTrackIntervalSeconds = 30 };
        await _service.SaveAsync();

        _service.Current = _service.Current with { AutoTrackIntervalSeconds = 60 };
        await _service.SaveAsync();

        _service.Current = _service.Current with { AutoTrackIntervalSeconds = 90 };
        await _service.SaveAsync();

        // Reload and verify the file is valid JSON with the latest value
        var reloaded = new ConfigService(_testDir);
        reloaded.Current.Version.Should().Be(1);
        reloaded.Current.AutoTrackIntervalSeconds.Should().Be(90);
    }

    // ── Migration: older config version ─────────────────────────

    [Fact]
    public async Task SaveAsync_PreservesAllFields()
    {
        _service.Current = new AppConfig
        {
            Version = 1,
            Language = "ja-JP",
            Theme = "Dark",
            DownloadDirectory = "/custom/downloads",
            AutoTrackIntervalSeconds = 120,
            DisclaimerDismissed = true,
            SteamPath = "/custom/steam"
        };
        await _service.SaveAsync();

        var reloaded = new ConfigService(_testDir);
        reloaded.Current.Version.Should().Be(1);
        reloaded.Current.Language.Should().Be("ja-JP");
        reloaded.Current.Theme.Should().Be("Dark");
        reloaded.Current.DownloadDirectory.Should().Be("/custom/downloads");
        reloaded.Current.AutoTrackIntervalSeconds.Should().Be(120);
        reloaded.Current.DisclaimerDismissed.Should().BeTrue();
        reloaded.Current.SteamPath.Should().Be("/custom/steam");
    }

    // ── Edge case: missing directory ────────────────────────────

    [Fact]
    public void Constructor_CreatesMissingDirectory()
    {
        var nonExistentDir = Path.Combine(_testDir, "subdir", "nested");

        var service = new ConfigService(nonExistentDir);

        Directory.Exists(nonExistentDir).Should().BeTrue();
        service.Current.Version.Should().Be(1);
    }

    // ── Edge case: empty config file ────────────────────────────

    [Fact]
    public void Constructor_EmptyConfigFile_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_testDir, "config.json"), string.Empty);

        var service = new ConfigService(_testDir);

        service.Current.Version.Should().Be(1);
        service.Current.Language.Should().Be("en-US");
    }

    // ── Edge case: null config JSON ─────────────────────────────

    [Fact]
    public void Constructor_NullJsonConfig_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_testDir, "config.json"), "null");

        var service = new ConfigService(_testDir);

        service.Current.Version.Should().Be(1);
    }

    // ── ReloadAsync edge cases ──────────────────────────────────

    [Fact]
    public async Task ReloadAsync_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var service = new ConfigService(Path.Combine(_testDir, "nonexistent"));
        service.Current = service.Current with { Language = "modified" };

        // Reload from non-existent dir should reset to defaults
        await service.ReloadAsync();

        service.Current.Language.Should().Be("en-US");
    }

    // ── GetStoragePath ──────────────────────────────────────────

    [Fact]
    public void GetStoragePath_WithSubdirectory_ReturnsCombinedPath()
    {
        var subPath = "sub" + Path.DirectorySeparatorChar + "db.json";
        var path = _service.GetStoragePath(subPath);

        path.Should().Be(Path.Combine(_testDir, subPath));
    }

    [Fact]
    public void GetStoragePath_WithEmptyString_ReturnsConfigDir()
    {
        var path = _service.GetStoragePath(string.Empty);

        // Path.Combine with empty string returns the config dir without trailing separator
        path.Should().Be(_testDir);
    }

    // ── SaveAsync with CancellationToken ────────────────────────

    [Fact]
    public async Task SaveAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _service.SaveAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }
}
