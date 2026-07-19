using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;

using SteamShare.Core.Localization;
using SteamShare.Core.Services;
using SteamShare.Core.Tasks;

namespace SteamShare.Core;

/// <summary>
/// DI registration extensions for SteamShare Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static readonly ILogger LogSerilog = Log.ForContext(typeof(ServiceCollectionExtensions));

    /// <summary>
    /// Registers all SteamShare Core services into the <see cref="IServiceCollection"/>.
    /// Uses <see cref="DummyWorkshopService"/> when <c>STEAMSHARE_TEST_MODE=dummy</c>,
    /// otherwise uses the real <see cref="SteamWorkshopService"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configPath">
    /// Directory for configuration and data files.
    /// Defaults to <c>%LocalAppData%/SteamShare</c> when null.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSteamShareCore(
        this IServiceCollection services,
        string? configPath = null)
    {
        var resolvedPath = configPath
            ?? Environment.GetEnvironmentVariable("STEAMSHARE_DATA_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamShare");

        LogSerilog.Information("Registering SteamShare Core services (configPath={ConfigPath})", resolvedPath);

        // ── Singleton registrations ──

        services.AddSingleton<IConfigService>(_ => new ConfigService(resolvedPath));
        services.AddSingleton(_ => new TrackingDatabaseService(resolvedPath));
        services.AddSingleton<SteamInitializer>();
        services.AddSingleton<SteamCallbackDispatcher>();
        services.AddSingleton<IShareKeyCryptoService, ShareKeyCryptoService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ITaskService, TaskService>();

        // Workshop: use dummy when STEAMSHARE_TEST_MODE=dummy
        if (Environment.GetEnvironmentVariable("STEAMSHARE_TEST_MODE") == "dummy")
        {
            LogSerilog.Information("SteamShare test mode (dummy) activated");
            services.AddSingleton<IWorkshopService, DummyWorkshopService>();
        }
        else
        {
            services.AddSingleton<IWorkshopService, SteamWorkshopService>();
        }

        // ── Transient registrations ──

        services.AddTransient<ShareService>();
        services.AddTransient<WorkshopQueryService>();
        services.AddTransient<VisibilityService>();
        services.AddTransient<FileGroupManager>();
        services.AddTransient<DownloadOrchestrator>();
        services.AddTransient<UploadOrchestrator>();

        // ── Hosted service ──

        services.AddSingleton<AutoTracker>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AutoTracker>());

        return services;
    }
}
