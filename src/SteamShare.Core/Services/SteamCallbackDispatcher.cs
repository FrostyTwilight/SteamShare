namespace SteamShare.Core.Services;

/// <summary>
/// Bridges Steamworks callbacks to async Tasks.
/// Delegates the callback dispatch loop to <see cref="CallResultUtils"/>.
/// Provides helper for creating TaskCompletionSource instances wired to cancellation.
/// </summary>
public sealed class SteamCallbackDispatcher : IDisposable
{
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Start the callback dispatch loop on a background thread.
    /// Must be called after SteamAPI.Init() succeeds.
    /// Idempotent — subsequent calls are no-ops.
    /// </summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        CallResultUtils.StartLoop();
    }

    /// <summary>
    /// Stop the dispatch loop.
    /// </summary>
    public Task StopAsync()
    {
        CallResultUtils.StopLoop();
        _started = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Dispose the dispatcher, stopping the loop if running.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            CallResultUtils.StopLoop();
            _disposed = true;
        }
    }
}
