using Serilog;

using Steamworks;

namespace SteamShare.Core.Services;

/// <summary>
/// Lightweight utilities for wrapping Steamworks CallResult-based APIs
/// into Tasks, and for running the SteamAPI callback dispatch loop.
/// </summary>
internal static class CallResultUtils
{
    private static readonly ILogger LogSerilog = Log.ForContext(typeof(CallResultUtils));
    private static bool steamShutdown = true;

    /// <summary>
    /// Start the SteamAPI callback dispatch loop on a dedicated long-running thread.
    /// </summary>
    public static void StartLoop()
    {
        LogSerilog.Debug("Starting SteamAPI callback dispatch loop");
        steamShutdown = false;
        Task.Factory.StartNew(() =>
        {
            while (!steamShutdown)
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(10);
            }
        }, TaskCreationOptions.LongRunning);
        LogSerilog.Information("SteamAPI callback dispatch loop started");
    }

    /// <summary>
    /// Signal the dispatch loop to stop. The loop thread will exit within ~10ms.
    /// </summary>
    public static void StopLoop()
    {
        LogSerilog.Debug("Stopping SteamAPI callback dispatch loop");
        steamShutdown = true;
    }

    /// <summary>
    /// Await a Steamworks CallResult as a Task.
    /// Returns the result on success or throws on I/O failure.
    /// </summary>
    public static Task<T> Wait<T>(this SteamAPICall_t callback)
    {
        if (steamShutdown)
        {
            throw new InvalidOperationException("SteamAPI is shutting down; cannot wait for callback.");
        }

        var source = new TaskCompletionSource<T>(TaskCreationOptions.None);
        var cr = CallResult<T>.Create();
        cr.Set(callback, (result, failed) =>
        {
            cr.Dispose();
            Task.Factory.StartNew(() =>
            {
                if (failed)
                {
                    source.SetException(new Exception());
                }
                else
                {
                    source.SetResult(result);
                }
            });
        });
        return source.Task;
    }
}
