using Serilog;
using Serilog.Events;

namespace SteamShare.Core.Services;

/// <summary>
/// Configures Serilog logging with console and file sinks.
/// Console: colored, compact format for development.
/// File: structured JSON, rolling daily, 10MB size limit, 31-day retention.
/// </summary>
public static class LogConfig
{
    /// <summary>
    /// Creates a Serilog logger configuration with console and file sinks.
    /// Call .CreateLogger() on the result to build the logger.
    /// </summary>
    /// <param name="logDirectory">Directory where log files will be written.</param>
    /// <param name="minimumLevel">Minimum log level. Default: Debug in DEBUG, Information otherwise.</param>
    public static LoggerConfiguration Create(string logDirectory, LogEventLevel? minimumLevel = null)
    {
        var level = ResolveLevel(minimumLevel);

        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        return new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code)
            .WriteTo.File(
                path: Path.Combine(logDirectory, $"steamshare-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
                fileSizeLimitBytes: 10_000_000, // 10 MB
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    }

    /// <summary>
    /// Creates a Serilog logger configuration with file sink only (no console).
    /// Used by CLI to avoid interfering with terminal output.
    /// </summary>
    /// <param name="logDirectory">Directory where log files will be written.</param>
    /// <param name="minimumLevel">Minimum log level. Default: Debug in DEBUG, Information otherwise.</param>
    public static LoggerConfiguration CreateFileOnly(string logDirectory, LogEventLevel? minimumLevel = null)
    {
        var level = ResolveLevel(minimumLevel);

        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        return new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(logDirectory, $"steamshare-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
                fileSizeLimitBytes: 10_000_000, // 10 MB
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    }

    private static LogEventLevel ResolveLevel(LogEventLevel? minimumLevel)
    {
        return minimumLevel
#if DEBUG
            ?? LogEventLevel.Debug;
#else
            ?? LogEventLevel.Information;
#endif
    }
}
