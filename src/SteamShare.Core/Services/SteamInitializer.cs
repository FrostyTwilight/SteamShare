using Serilog;

namespace SteamShare.Core.Services;

/// <summary>
/// Result of Steam API initialization attempt.
/// </summary>
public enum SteamInitResult
{
    Success,
    SteamNotRunning,
    SteamNotLoggedIn,
    NativeDllNotFound,
    PlatformMismatch,
    UnknownError
}

/// <summary>
/// Handles Steam API initialization, native DLL resolution, and platform checks.
/// Sets the Steam AppId via Environment.SetEnvironmentVariable before init.
/// </summary>
public sealed class SteamInitializer
{
    private static readonly ILogger LogSerilog = Log.ForContext<SteamInitializer>();
    private bool _initialized;
    private readonly uint _appId;

    public const uint DefaultAppId = 480; // Spacewar

    public SteamInitializer(uint appId = DefaultAppId)
    {
        _appId = appId;
    }

    /// <summary>
    /// Initialize the Steam API. Safe to call multiple times — returns cached result.
    /// </summary>
    public SteamInitResult Initialize()
    {
        if (_initialized)
        {
            return SteamInitResult.Success;
        }

        LogSerilog.Information("Initializing Steam API with AppId {AppId}", _appId);

        try
        {
            // Set AppId before initialization
            Environment.SetEnvironmentVariable("SteamAppId", _appId.ToString());

            // Register native DLL resolver for cross-platform support
            RegisterNativeResolver();

            // Initialize Steam API
            if (!Steamworks.SteamAPI.Init())
            {
                // Check common failure reasons
                LogSerilog.Warning("Steam API Init returned false — Steam client may not be running");
                return SteamInitResult.SteamNotRunning;
            }

            // Verify platform compatibility
            if (!Steamworks.Packsize.Test())
            {
                LogSerilog.Error("Platform mismatch: Packsize.Test failed");
                Steamworks.SteamAPI.Shutdown();
                return SteamInitResult.PlatformMismatch;
            }

            if (!Steamworks.DllCheck.Test())
            {
                LogSerilog.Error("Platform mismatch: DllCheck.Test failed");
                Steamworks.SteamAPI.Shutdown();
                return SteamInitResult.PlatformMismatch;
            }

            _initialized = true;
            LogSerilog.Information("Steam API initialized successfully");
            return SteamInitResult.Success;
        }
        catch (DllNotFoundException ex)
        {
            LogSerilog.Error(ex, "Steam native DLL not found");
            return SteamInitResult.NativeDllNotFound;
        }
        catch (Exception ex)
        {
            LogSerilog.Error(ex, "Unexpected error during Steam initialization");
            return SteamInitResult.UnknownError;
        }
    }

    /// <summary>
    /// Shutdown the Steam API. Safe to call even if not initialized.
    /// </summary>
    public void Shutdown()
    {
        if (_initialized)
        {
            LogSerilog.Information("Shutting down Steam API");
            Steamworks.SteamAPI.Shutdown();
            _initialized = false;
        }
    }

    /// <summary>
    /// Pump Steam callbacks. Must be called regularly from the main thread.
    /// </summary>
    public void RunCallbacks()
    {
        if (_initialized)
        {
            Steamworks.SteamAPI.RunCallbacks();
        }
    }

    public bool IsInitialized => _initialized;

    /// <summary>
    /// Register a native library resolver so Steamworks.NET can find
    /// the correct steam_api native DLL on each platform.
    /// Critical for .NET Core on Linux where the default search path
    /// doesn't include the application directory.
    /// </summary>
    private static void RegisterNativeResolver()
    {
        // On .NET Core, the native DLL search path differs from .NET Framework.
        // Steamworks.NET P/Invokes "steam_api64" (Windows) or "steam_api" (Linux/macOS).
        // We register a resolver so it finds the DLL next to the executable.
        if (OperatingSystem.IsWindows())
        {
            // Windows: the standard search path includes the app directory,
            // but we set it explicitly for reliability
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var dllPath = Path.Combine(appDir, "steam_api64.dll");
            if (File.Exists(dllPath))
            {
                System.Runtime.InteropServices.NativeLibrary.Load(dllPath);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var dllPath = Path.Combine(appDir, "libsteam_api.so");
            if (File.Exists(dllPath))
            {
                System.Runtime.InteropServices.NativeLibrary.Load(dllPath);
            }
        }
    }
}
