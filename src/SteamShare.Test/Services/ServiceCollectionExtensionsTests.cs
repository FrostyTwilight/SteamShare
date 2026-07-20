using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using SteamShare.Core;
using SteamShare.Core.Services;
using SteamShare.Core.Tasks;

namespace SteamShare.Test.Services;

public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _testDir;
    private readonly ServiceProvider _provider;
    private readonly string? _originalTestMode;

    public ServiceCollectionExtensionsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SteamShareTest_{Guid.NewGuid():N}");

        // Save and restore env var to avoid leaking to other tests running in parallel.
        _originalTestMode = Environment.GetEnvironmentVariable("STEAMSHARE_TEST_MODE");
        Environment.SetEnvironmentVariable("STEAMSHARE_TEST_MODE", "dummy");

        try
        {
            var services = new ServiceCollection();
            services.AddSteamShareCore(_testDir);
            _provider = services.BuildServiceProvider();
        }
        catch
        {
            // Restore env var immediately if construction fails, then rethrow.
            Environment.SetEnvironmentVariable("STEAMSHARE_TEST_MODE", _originalTestMode);
            throw;
        }
    }

    [Fact]
    public void All_Core_Services_Resolve()
    {
        // Singleton services
        var config = _provider.GetRequiredService<IConfigService>();
        config.Should().NotBeNull().And.BeOfType<ConfigService>();

        var trackingDb = _provider.GetRequiredService<TrackingDatabaseService>();
        trackingDb.Should().NotBeNull().And.BeOfType<TrackingDatabaseService>();

        var workshop = _provider.GetRequiredService<IWorkshopService>();
        workshop.Should().NotBeNull();
        workshop.IsSteamRunning.Should().BeFalse(); // not initialized yet

        var steamInit = _provider.GetRequiredService<SteamInitializer>();
        steamInit.Should().NotBeNull().And.BeOfType<SteamInitializer>();

        var dispatcher = _provider.GetRequiredService<SteamCallbackDispatcher>();
        dispatcher.Should().NotBeNull().And.BeOfType<SteamCallbackDispatcher>();

        var crypto = _provider.GetRequiredService<IShareKeyCryptoService>();
        crypto.Should().NotBeNull().And.BeOfType<ShareKeyCryptoService>();

        var taskService = _provider.GetRequiredService<ITaskService>();
        taskService.Should().NotBeNull().And.BeOfType<TaskService>();

        // Transient services
        var share = _provider.GetRequiredService<ShareService>();
        share.Should().NotBeNull().And.BeOfType<ShareService>();

        var query = _provider.GetRequiredService<WorkshopQueryService>();
        query.Should().NotBeNull().And.BeOfType<WorkshopQueryService>();

        var visibility = _provider.GetRequiredService<VisibilityService>();
        visibility.Should().NotBeNull().And.BeOfType<VisibilityService>();

        var fileGroup = _provider.GetRequiredService<FileGroupManager>();
        fileGroup.Should().NotBeNull().And.BeOfType<FileGroupManager>();

        // Hosted service
        var hostedServices = _provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle()
            .Which.Should().BeOfType<AutoTracker>();
    }

    [Fact]
    public void Singleton_Services_Return_Same_Instance()
    {
        var config1 = _provider.GetRequiredService<IConfigService>();
        var config2 = _provider.GetRequiredService<IConfigService>();
        config1.Should().BeSameAs(config2);

        var trackingDb1 = _provider.GetRequiredService<TrackingDatabaseService>();
        var trackingDb2 = _provider.GetRequiredService<TrackingDatabaseService>();
        trackingDb1.Should().BeSameAs(trackingDb2);

        var workshop1 = _provider.GetRequiredService<IWorkshopService>();
        var workshop2 = _provider.GetRequiredService<IWorkshopService>();
        workshop1.Should().BeSameAs(workshop2);

        var crypto1 = _provider.GetRequiredService<IShareKeyCryptoService>();
        var crypto2 = _provider.GetRequiredService<IShareKeyCryptoService>();
        crypto1.Should().BeSameAs(crypto2);

        var taskService1 = _provider.GetRequiredService<ITaskService>();
        var taskService2 = _provider.GetRequiredService<ITaskService>();
        taskService1.Should().BeSameAs(taskService2);
    }

    [Fact]
    public void Transient_Services_Return_Different_Instances()
    {
        var share1 = _provider.GetRequiredService<ShareService>();
        var share2 = _provider.GetRequiredService<ShareService>();
        share1.Should().NotBeSameAs(share2);

        var query1 = _provider.GetRequiredService<WorkshopQueryService>();
        var query2 = _provider.GetRequiredService<WorkshopQueryService>();
        query1.Should().NotBeSameAs(query2);

        var vis1 = _provider.GetRequiredService<VisibilityService>();
        var vis2 = _provider.GetRequiredService<VisibilityService>();
        vis1.Should().NotBeSameAs(vis2);

        var fg1 = _provider.GetRequiredService<FileGroupManager>();
        var fg2 = _provider.GetRequiredService<FileGroupManager>();
        fg1.Should().NotBeSameAs(fg2);
    }

    [Fact]
    public void AutoTracker_Registered_As_HostedService()
    {
        var hostedServices = _provider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle()
            .Which.Should().BeOfType<AutoTracker>();
    }

    [Fact]
    public void AddSteamShareCore_Returns_Same_ServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddSteamShareCore(_testDir);
        result.Should().BeSameAs(services);
    }

    public void Dispose()
    {
        // Restore original env var value (null when it was not set).
        Environment.SetEnvironmentVariable("STEAMSHARE_TEST_MODE", _originalTestMode);
        _provider.Dispose();

        if (!Directory.Exists(_testDir))
        {
            return;
        }

        // Retry with backoff — on Windows, SQLite journal files may not be
        // released immediately after the last connection is disposed.
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(_testDir, true);
                return;
            }
            catch (IOException) when (i < 4)
            {
                Thread.Sleep(50 * (i + 1));
            }
            catch (UnauthorizedAccessException) when (i < 4)
            {
                Thread.Sleep(50 * (i + 1));
            }
        }
    }
}
