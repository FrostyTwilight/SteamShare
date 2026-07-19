using Microsoft.Extensions.Hosting;

using Serilog;

using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

public record TrackingChange(ulong PublishedFileId, string ChangeType, string? Details);

public class AutoTracker : IHostedService, IDisposable
{
    private static readonly ILogger LogSerilog = Log.ForContext<AutoTracker>();
    private readonly IWorkshopService _workshop;
    private readonly TrackingDatabaseService _trackingDb;
    private readonly FileGroupManager _fileGroupManager;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private PeriodicTimer? _timer;
    private Task? _executingTask;
    private bool _disposed;

    public event Action<TrackingChange>? OnChange;

    public TimeSpan Interval => _interval;

    public AutoTracker(
        IWorkshopService workshop,
        TrackingDatabaseService trackingDb,
        FileGroupManager fileGroupManager,
        TimeSpan? interval = null)
    {
        _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
        _trackingDb = trackingDb ?? throw new ArgumentNullException(nameof(trackingDb));
        _fileGroupManager = fileGroupManager ?? throw new ArgumentNullException(nameof(fileGroupManager));
        _interval = interval ?? TimeSpan.FromSeconds(60);
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_executingTask is not null)
        {
            return Task.CompletedTask;
        }

        LogSerilog.Information("AutoTracker starting with interval {Interval}s", _interval.TotalSeconds);
        _timer = new PeriodicTimer(_interval);
        _executingTask = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_executingTask is null)
        {
            return;
        }

        LogSerilog.Information("AutoTracker stopping");
        _cts.Cancel();

        try
        {
            await _executingTask.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Expected — the task was cancelled
        }
        catch (Exception ex)
        {
            // Unexpected exception during tick (e.g. SQLite busy, transient I/O error).
            // Log it but don't propagate — the tick will naturally stop on next iteration.
            LogSerilog.Error(ex, "AutoTracker tick threw an unexpected exception during shutdown");
        }

        _executingTask = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _timer?.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await TickAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!_workshop.IsSteamRunning)
        {
            return;
        }

        LogSerilog.Debug("AutoTracker tick started");
        var workshopItems = await _workshop.QueryOwnedItemsAsync(ct).ConfigureAwait(false);
        var trackingEntries = _trackingDb.GetAll();

        var workshopIds = new HashSet<ulong>(workshopItems.Count);
        var workshopItemMap = new Dictionary<ulong, WorkshopItemInfo>(workshopItems.Count);
        foreach (var item in workshopItems)
        {
            workshopIds.Add(item.PublishedFileId);
            workshopItemMap[item.PublishedFileId] = item;
        }

        var trackingIds = new HashSet<ulong>(trackingEntries.Count);
        foreach (var entry in trackingEntries)
        {
            trackingIds.Add(entry.PublishedFileId);
        }

        // Detect new items: in workshop but not in tracking DB
        foreach (var item in workshopItems)
        {
            if (!trackingIds.Contains(item.PublishedFileId))
            {
                var itemName = item.Description ?? item.Title;
                LogSerilog.Information("AutoTracker: new item detected {PublishedFileId} ({Name})",
                    item.PublishedFileId, itemName);
                OnChange?.Invoke(new TrackingChange(item.PublishedFileId, "Added", itemName));
                _trackingDb.Upsert(new FileGroupTrackingEntry
                {
                    PublishedFileId = item.PublishedFileId,
                    LocalPath = null,
                    State = DownloadState.NotDownloaded,
                    CachedName = itemName,
                    CachedVisibility = item.Visibility,
                    LastSyncedAt = DateTimeOffset.UtcNow
                });
            }
        }

        // Detect deleted items: in tracking DB but not in workshop
        foreach (var entry in trackingEntries)
        {
            if (!workshopIds.Contains(entry.PublishedFileId))
            {
                LogSerilog.Information("AutoTracker: item deleted {PublishedFileId} ({Name})",
                    entry.PublishedFileId, entry.CachedName);
                OnChange?.Invoke(new TrackingChange(entry.PublishedFileId, "Deleted", entry.CachedName));
                _trackingDb.Remove(entry.PublishedFileId);
            }
        }

        // Detect changed items: in both but properties differ
        foreach (var entry in trackingEntries)
        {
            if (!workshopItemMap.TryGetValue(entry.PublishedFileId, out var item))
            {
                continue;
            }

            var changed = false;
            var changes = new List<string>();

            if (entry.CachedVisibility != item.Visibility)
            {
                changed = true;
                changes.Add($"Visibility: {entry.CachedVisibility} -> {item.Visibility}");
            }

            var itemName = item.Description ?? item.Title;
            if (!string.Equals(entry.CachedName, itemName, StringComparison.Ordinal))
            {
                changed = true;
                changes.Add($"Name: '{entry.CachedName}' -> '{itemName}'");
            }

            if (changed)
            {
                var details = string.Join(", ", changes);
                LogSerilog.Information("AutoTracker: item changed {PublishedFileId} ({Changes})",
                    entry.PublishedFileId, details);
                OnChange?.Invoke(new TrackingChange(entry.PublishedFileId, "Changed", details));
                _trackingDb.Upsert(entry with
                {
                    CachedName = itemName,
                    CachedVisibility = item.Visibility,
                    LastSyncedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await _trackingDb.SaveAsync(ct).ConfigureAwait(false);
        LogSerilog.Debug("AutoTracker tick completed");
    }
}
