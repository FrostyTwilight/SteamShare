using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using SteamShare.CLI;
using SteamShare.CLI.Commands;
using SteamShare.Core;
using SteamShare.Core.Localization;
using SteamShare.Core.Services;

// ═══════════════════════════════════════════════════════════════════
// Bootstrap Logger
// ═══════════════════════════════════════════════════════════════════
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    SteamShareCore.AppName,
    "logs");
Log.Logger = LogConfig.CreateFileOnly(logDir).CreateLogger();

SteamInitializer? steamInit = null;

try
{
    // ═══════════════════════════════════════════════════════════════
    // Application Host
    // ═══════════════════════════════════════════════════════════════
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger);
    builder.Services.AddSteamShareCore();

    using var host = builder.Build();
    AppServices.Provider = host.Services;

    // ═══════════════════════════════════════════════════════════════
    // Disclaimer
    // ═══════════════════════════════════════════════════════════════
    var isHelpOrVersion = Array.Exists(args, a =>
        a is "-h" or "--help" or "--version" or "-v");

    var acceptDisclaimer = Array.Exists(args, a =>
        a.Equals("--accept-disclaimer", StringComparison.OrdinalIgnoreCase));

    var config = host.Services.GetRequiredService<IConfigService>();
    var loc = host.Services.GetRequiredService<LocalizationService>();

    if (acceptDisclaimer && !config.Current.DisclaimerDismissed)
    {
        if (config is ConfigService configSvc)
        {
            configSvc.Current = config.Current with { DisclaimerDismissed = true };
            await config.SaveAsync();
        }
    }

    if (!isHelpOrVersion && !acceptDisclaimer && !config.Current.DisclaimerDismissed)
    {
        ShowDisclaimer(loc);
        return 1;
    }

    // ═══════════════════════════════════════════════════════════════
    // Steam Initialization
    // ═══════════════════════════════════════════════════════════════
    var isTestMode = Environment.GetEnvironmentVariable("STEAMSHARE_TEST_MODE") == "dummy";

    if (!isHelpOrVersion && !isTestMode)
    {
        steamInit = host.Services.GetRequiredService<SteamInitializer>();
        var initResult = steamInit.Initialize();

        if (initResult != SteamInitResult.Success)
        {
            ShowSteamError(initResult, loc);
            return 1;
        }

        await host.Services.GetRequiredService<IWorkshopService>().InitializeAsync();

        Log.Information("Steam API initialized successfully");
    }
    else if (isTestMode)
    {
        await host.Services.GetRequiredService<IWorkshopService>().InitializeAsync();
        Log.Information("Steam API initialized in test mode (dummy)");
    }

    // ═══════════════════════════════════════════════════════════════
    // CLI Command Routing
    // ═══════════════════════════════════════════════════════════════
    var app = new CommandApp();
    app.Configure(config =>
    {
        config.PropagateExceptions();

        config.SetApplicationName("steamshare");
        config.SetApplicationVersion(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion);

        config.AddCommand<UploadCommand>("upload")
            .WithDescription("Upload a directory as a file group");
        config.AddCommand<DownloadCommand>("download")
            .WithDescription("Download via share key or published file ID");
        config.AddCommand<ShareCommand>("share")
            .WithDescription("Generate a share key for a file group");
        config.AddCommand<ListCommand>("list")
            .WithDescription("List owned file groups");
        config.AddCommand<DeleteCommand>("delete")
            .WithDescription("Delete a file group");
        config.AddCommand<RenameCommand>("rename")
            .WithDescription("Rename a file group");
        config.AddCommand<VisibilityCommand>("visibility")
            .WithDescription("Change visibility of a file group");
    });

    // Strip --accept-disclaimer from args before Spectre parses them
    var filteredArgs = Array.FindAll(args, a =>
        !a.Equals("--accept-disclaimer", StringComparison.OrdinalIgnoreCase));

    return app.Run(filteredArgs);
}
catch (CommandRuntimeException ex)
{
    Log.Error(ex, "Command runtime error");
    if (ex.Pretty != null)
    {
        AnsiConsole.Write(ex.Pretty);
    }
    else
    {
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes
            | ExceptionFormats.ShortenMethods | ExceptionFormats.ShowLinks);
    }

    return 1;
}
catch (Exception ex)
{
    Log.Error(ex, "Unhandled CLI exception");
    AnsiConsole.WriteException(ex);
    return 1;
}
finally
{
    steamInit?.Shutdown();
    Log.CloseAndFlush();
}

// ═══════════════════════════════════════════════════════════════════
// Helper Methods
// ═══════════════════════════════════════════════════════════════════

static void ShowDisclaimer(LocalizationService loc)
{
    AnsiConsole.MarkupLine($"[bold yellow]{loc.GetString("CLI_Disclaimer_Title")}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine(loc.GetString("CLI_Disclaimer_Line1"));
    AnsiConsole.MarkupLine(loc.GetString("CLI_Disclaimer_Line2"));
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"  • {loc.GetString("CLI_Disclaimer_Bullet1")}");
    AnsiConsole.MarkupLine($"  • {loc.GetString("CLI_Disclaimer_Bullet2")}");
    AnsiConsole.MarkupLine($"  • {loc.GetString("CLI_Disclaimer_Bullet3")}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[cyan]{loc.GetString("CLI_Disclaimer_SuppressHint")}[/]");
}

static void ShowSteamError(SteamInitResult result, LocalizationService loc)
{
    var message = result switch
    {
        SteamInitResult.SteamNotRunning =>
            loc.GetString("Error_SteamNotRunning"),
        SteamInitResult.SteamNotLoggedIn =>
            loc.GetString("Error_SteamNotLoggedIn"),
        SteamInitResult.NativeDllNotFound =>
            loc.GetString("Error_NativeDllNotFound"),
        SteamInitResult.PlatformMismatch =>
            loc.GetString("Error_PlatformMismatch"),
        _ => loc.GetString("Error_Unknown")
    };

    AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
    Log.Error("Steam initialization failed: {Result}", result);
}
