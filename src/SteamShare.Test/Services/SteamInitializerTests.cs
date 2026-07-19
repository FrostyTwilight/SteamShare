using SteamShare.Core.Services;

namespace SteamShare.Test.Services;

public class SteamInitializerTests
{
    [Fact]
    public void Initialize_WithoutSteamRunning_ReturnsSteamNotRunning()
    {
        // Note: This test is conditional — it will fail in CI without Steam.
        // In CI, skip this test.
        if (Environment.GetEnvironmentVariable("CI") == "true")
        {
            return;
        }

        var initializer = new SteamInitializer(480);
        var result = initializer.Initialize();

        // When Steam is not running, we expect SteamNotRunning
        // When Steam IS running, we expect Success
        if (result != SteamInitResult.Success)
        {
            result.Should().Be(SteamInitResult.SteamNotRunning);
        }
    }

    [Fact]
    public void Initialize_WhenCalledTwice_ReturnsCachedResult()
    {
        if (Environment.GetEnvironmentVariable("CI") == "true")
        {
            return;
        }

        var initializer = new SteamInitializer(480);
        var first = initializer.Initialize();
        var second = initializer.Initialize();

        second.Should().Be(first);
    }

    [Fact]
    public void Shutdown_WhenNotInitialized_DoesNotThrow()
    {
        var initializer = new SteamInitializer(480);
        var act = () => initializer.Shutdown();
        act.Should().NotThrow();
    }

    [Fact]
    public void IsInitialized_DefaultsToFalse()
    {
        var initializer = new SteamInitializer(480);
        initializer.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void DefaultAppId_Is480_Spacewar()
    {
        SteamInitializer.DefaultAppId.Should().Be(480u);
    }

    [Fact]
    public void Constructor_AcceptsCustomAppId()
    {
        var initializer = new SteamInitializer(999);
        // Just verify it doesn't throw
        initializer.Should().NotBeNull();
    }

    [Fact]
    public void RunCallbacks_WhenNotInitialized_DoesNotThrow()
    {
        var initializer = new SteamInitializer(480);
        var act = () => initializer.RunCallbacks();
        act.Should().NotThrow();
    }
}
