using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

using SteamShare.Core;
using SteamShare.Core.Localization;
using SteamShare.Core.Services;
using SteamShare.Core.Tasks;
using SteamShare.UI.Services;
using SteamShare.UI.ViewModels;
using SteamShare.UI.Views;

namespace SteamShare.UI;

public class App : Application
{
    private static readonly ILogger LogSerilog = Log.ForContext<App>();

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Configure Serilog for UI: console + file
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SteamShareCore.AppName,
                "logs");
            Log.Logger = LogConfig.Create(logDir).CreateLogger();
            LogSerilog.Information("=== SteamShare UI starting ===");
            LogSerilog.Information("Log directory: {LogDir}", logDir);

            // Set Steam AppId for Spacewar (480) before initializing Steam
            Environment.SetEnvironmentVariable("SteamAppId", "480");

            var services = new ServiceCollection();

            // Core services (Steam, workshop, crypto, tracking, etc.)
            services.AddSteamShareCore();

            // UI services
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<MainViewModel>();

            // ViewModels (transient — new instances on each navigation)
            services.AddTransient<TasksViewModel>();
            services.AddTransient<MyFileGroupsViewModel>();
            services.AddTransient<DownloadedViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AboutViewModel>();

            var serviceProvider = services.BuildServiceProvider();

            // Static accessor for ViewModel locator and DI resolution
            AppServices.Provider = serviceProvider;

            // Apply theme from config on startup
            var configService = serviceProvider.GetRequiredService<IConfigService>();
            var themeService = serviceProvider.GetRequiredService<ThemeService>();
            LogSerilog.Information("Applying theme: {Theme}", configService.Current.Theme);
            themeService.ApplyTheme(configService.Current.Theme);

            // Initialize notification service for toast display
            var notificationService = (NotificationService)serviceProvider.GetRequiredService<INotificationService>();

            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();

            // Global exception handler
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                LogSerilog.Error(ex, "Unhandled exception (AppDomain)");
                var loc = AppServices.Provider.GetRequiredService<LocalizationService>();
                ShowErrorDialog(ex?.Message ?? loc.GetString("Error_UnknownGeneric"));
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                LogSerilog.Error(args.Exception, "Unobserved task exception");
                ShowErrorDialog(args.Exception.Message);
                args.SetObserved();
            };

            var loc = serviceProvider.GetRequiredService<LocalizationService>();
            var conf = serviceProvider.GetRequiredService<IConfigService>();
            loc.SetCulture(conf.Current.Language);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };



            // Initialize Steam after the window is created
            _ = InitializeSteam(serviceProvider, notificationService);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeSteam(IServiceProvider sp, NotificationService notificationService)
    {
        try
        {
            var mainVm = sp.GetRequiredService<MainViewModel>();

            // When running in test mode, skip real Steam initialization
            if (Environment.GetEnvironmentVariable("STEAMSHARE_TEST_MODE") == "dummy")
            {
                LogSerilog.Information("Steam init skipped: test mode (dummy) active");
                mainVm.IsSteamConnected = false;
                return;
            }

            LogSerilog.Information("Initializing Steam API");
            var steamInit = sp.GetRequiredService<SteamInitializer>();
            var result = steamInit.Initialize();

            mainVm.IsSteamConnected = result == SteamInitResult.Success;

            if (result != SteamInitResult.Success)
            {
                LogSerilog.Warning("Steam init failed: {Result}", result);
                var loc = sp.GetRequiredService<LocalizationService>();
                notificationService.ShowWarning(loc.GetString("Error_SteamNotConnected"));
            }
            else
            {
                LogSerilog.Information("Steam API initialized successfully");
            }

            var workshop = sp.GetRequiredService<IWorkshopService>();
            await workshop.InitializeAsync();

            // Restart pending download/upload tasks from previous session
            _ = RestartPendingTasksAsync(sp);
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Steam initialization threw exception");
            var mainVm = sp.GetRequiredService<MainViewModel>();
            mainVm.IsSteamConnected = false;
            var loc = sp.GetRequiredService<LocalizationService>();
            notificationService.ShowWarning(loc.GetString("Error_SteamInitFailed", ex.Message));
        }
    }

    private static void ShowErrorDialog(string message)
    {
        try
        {
            var dialog = new ErrorDialog(message);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow != null)
            {
                _ = dialog.ShowDialog(desktop.MainWindow);
            }
            else
            {
                dialog.Show();
            }
        }
        catch (Exception ex)
        {
            // Last resort — can't show dialog, log to Serilog
            LogSerilog.Error(ex, "FATAL: Could not show error dialog. Original message: {Message}", message);
        }
    }

    private static async Task RestartPendingTasksAsync(IServiceProvider sp)
    {
        try
        {
            var configService = sp.GetRequiredService<IConfigService>();
            if (!configService.Current.AutoRestartPendingTasks)
            {
                LogSerilog.Information("Auto-restart pending tasks is disabled — clearing pending tasks from DB");
                var trackingDb = sp.GetRequiredService<TrackingDatabaseService>();
                trackingDb.ClearPendingTasks();
                return;
            }

            var downloadOrch = sp.GetRequiredService<DownloadOrchestrator>();
            var pendingDownloads = downloadOrch.GetPendingDownloads();
            foreach (var task in pendingDownloads)
            {
                try
                {
                    LogSerilog.Information("Restarting pending download (id={Id})", task.Id);
                    await downloadOrch.RestartPendingDownloadAsync(task);
                }
                catch (Exception ex)
                {
                    LogSerilog.Error(ex, "Failed to restart pending download (id={Id})", task.Id);
                }
            }

            var uploadOrch = sp.GetRequiredService<UploadOrchestrator>();
            var pendingUploads = uploadOrch.GetPendingUploads();
            foreach (var task in pendingUploads)
            {
                try
                {
                    LogSerilog.Information("Restarting pending upload (id={Id})", task.Id);
                    await uploadOrch.RestartPendingUploadAsync(task);
                }
                catch (Exception ex)
                {
                    LogSerilog.Error(ex, "Failed to restart pending upload (id={Id})", task.Id);
                }
            }
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Failed to restart pending tasks on startup");
        }
    }

#if DEBUG
    public override void Initialize()
    {
        try
        {
            this.AttachDeveloperTools();
        }
        catch
        {

        }
        base.Initialize();
    }
#endif
}

/// <summary>
/// Static service provider accessor for cases where DI is not available
/// (e.g. ViewModel locator patterns).
/// </summary>
public static class AppServices
{
    public static IServiceProvider Provider { get; set; } = null!;
}

/// <summary>
/// Helper extension to get the main window from the current application.
/// </summary>
public static class AppExtensions
{
    public static Window? GetMainWindow(this Application app)
    {
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}
