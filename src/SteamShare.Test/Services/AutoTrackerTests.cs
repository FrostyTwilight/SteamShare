using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.Test.Dummy;

using DummyWorkshopService = SteamShare.Test.Dummy.DummyWorkshopService;

namespace SteamShare.Test.Services;

public class AutoTrackerTests : IDisposable
{
    private readonly DummyWorkshopService _workshop;
    private readonly TrackingDatabaseService _trackingDb;
    private readonly FileGroupManager _fileGroupManager;
    private readonly string _tempDataDir;

    public AutoTrackerTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "SteamShare_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        _workshop = new DummyWorkshopService();
        _trackingDb = new TrackingDatabaseService(_tempDataDir);
        _fileGroupManager = new FileGroupManager(_workshop, _trackingDb);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDataDir))
        {
            return;
        }

        // Retry with backoff — on Windows, SQLite file handles and journal files
        // may not be released immediately after the last connection is disposed.
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
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

    // ── Constructor ─────────────────────────────────────────────

    [Fact]
    public void Constructor_ShouldAcceptValidDependencies()
    {
        var act = () => new AutoTracker(_workshop, _trackingDb, _fileGroupManager);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenWorkshopIsNull()
    {
        var act = () => new AutoTracker(null!, _trackingDb, _fileGroupManager);

        act.Should().Throw<ArgumentNullException>().WithParameterName("workshop");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTrackingDbIsNull()
    {
        var act = () => new AutoTracker(_workshop, null!, _fileGroupManager);

        act.Should().Throw<ArgumentNullException>().WithParameterName("trackingDb");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFileGroupManagerIsNull()
    {
        var act = () => new AutoTracker(_workshop, _trackingDb, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("fileGroupManager");
    }

    [Fact]
    public void Constructor_ShouldUseDefaultInterval_WhenNotSpecified()
    {
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager);

        tracker.Interval.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Constructor_ShouldUseProvidedInterval()
    {
        var customInterval = TimeSpan.FromSeconds(30);

        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, customInterval);

        tracker.Interval.Should().Be(customInterval);
    }

    // ── StartAsync ──────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_ShouldReturnCompletedTask()
    {
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager);

        var task = tracker.StartAsync(CancellationToken.None);
        await task; // Should not throw or block

        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_ShouldNotThrow_WhenCalledMultipleTimes()
    {
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager);

        await tracker.StartAsync(CancellationToken.None);
        var act = () => tracker.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── OnTick: Steam not initialized ───────────────────────────

    [Fact]
    public async Task OnTick_ShouldSkip_WhenSteamNotInitialized()
    {
        await _workshop.ShutdownAsync(); // Ensure not initialized
        _workshop.IsSteamRunning.Should().BeFalse();

        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change => receivedEvents.Add(change);

        await tracker.StartAsync(CancellationToken.None);

        // Wait a few ticks worth of time
        await Task.Delay(100);

        await tracker.StopAsync(CancellationToken.None);

        receivedEvents.Should().BeEmpty("no ticks should fire events when Steam is not initialized");
    }

    // ── OnTick: New items ──────────────────────────────────────

    [Fact]
    public async Task OnTick_ShouldDetectNewItems()
    {
        await _workshop.InitializeAsync();
        var tcs = new TaskCompletionSource<TrackingChange>();

        // Create workshop items
        var id1 = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Item1",
            Description = "Description1",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });
        var id2 = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Item2",
            Description = "Description2",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Public
        });

        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change =>
        {
            receivedEvents.Add(change);
            if (receivedEvents.Count >= 2)
            {
                tcs.TrySetResult(change);
            }
        };

        await tracker.StartAsync(CancellationToken.None);

        // Wait for at least 2 events with timeout
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task, "should receive change events within timeout");

        await tracker.StopAsync(CancellationToken.None);

        receivedEvents.Should().HaveCount(2);
        receivedEvents.Should().Contain(e => e.PublishedFileId == id1 && e.ChangeType == "Added");
        receivedEvents.Should().Contain(e => e.PublishedFileId == id2 && e.ChangeType == "Added");

        // Tracking DB should be updated
        var entry1 = _trackingDb.GetByPublishedFileId(id1);
        entry1.Should().NotBeNull();
        entry1!.CachedName.Should().Be("Description1");
        entry1.CachedVisibility.Should().Be(WorkshopVisibility.Private);

        var entry2 = _trackingDb.GetByPublishedFileId(id2);
        entry2.Should().NotBeNull();
        entry2!.CachedName.Should().Be("Description2");
        entry2.CachedVisibility.Should().Be(WorkshopVisibility.Public);
    }

    // ── OnTick: Deleted items ───────────────────────────────────

    [Fact]
    public async Task OnTick_ShouldDetectDeletedItems()
    {
        await _workshop.InitializeAsync();

        // Add items to tracking DB (but NOT to workshop — simulating deletion)
        var orphanId = 99999UL;
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = orphanId,
            CachedName = "OrphanItem",
            CachedVisibility = WorkshopVisibility.Private,
            State = DownloadState.Downloaded
        });
        await _trackingDb.SaveAsync();

        var tcs = new TaskCompletionSource<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change =>
        {
            if (change.ChangeType == "Deleted")
            {
                tcs.TrySetResult(change);
            }
        };

        await tracker.StartAsync(CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task, "should receive deleted event within timeout");

        await tracker.StopAsync(CancellationToken.None);

        tcs.Task.Result.PublishedFileId.Should().Be(orphanId);
        tcs.Task.Result.ChangeType.Should().Be("Deleted");
        tcs.Task.Result.Details.Should().Be("OrphanItem");

        // Tracking DB should have the item removed
        _trackingDb.GetByPublishedFileId(orphanId).Should().BeNull();
    }

    // ── OnTick: Changed items ──────────────────────────────────

    [Fact]
    public async Task OnTick_ShouldDetectChangedVisibility()
    {
        await _workshop.InitializeAsync();

        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Item",
            Description = "Desc",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });

        // Add matching tracking entry with DIFFERENT visibility
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = id,
            CachedName = "Desc",
            CachedVisibility = WorkshopVisibility.Public, // Different!
            State = DownloadState.Downloaded
        });
        await _trackingDb.SaveAsync();

        var tcs = new TaskCompletionSource<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change =>
        {
            if (change.ChangeType == "Changed")
            {
                tcs.TrySetResult(change);
            }
        };

        await tracker.StartAsync(CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task, "should receive changed event within timeout");

        await tracker.StopAsync(CancellationToken.None);

        tcs.Task.Result.PublishedFileId.Should().Be(id);
        tcs.Task.Result.ChangeType.Should().Be("Changed");
        tcs.Task.Result.Details.Should().Contain("Visibility");

        // Tracking DB should be updated with correct visibility
        var entry = _trackingDb.GetByPublishedFileId(id);
        entry.Should().NotBeNull();
        entry!.CachedVisibility.Should().Be(WorkshopVisibility.Private);
    }

    [Fact]
    public async Task OnTick_ShouldDetectChangedName()
    {
        await _workshop.InitializeAsync();

        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "NewTitle",
            Description = "NewName", // Workshop has "NewName"
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });

        // Add matching tracking entry with DIFFERENT name
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = id,
            CachedName = "OldName", // Different!
            CachedVisibility = WorkshopVisibility.Private,
            State = DownloadState.Downloaded
        });
        await _trackingDb.SaveAsync();

        var tcs = new TaskCompletionSource<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change =>
        {
            if (change.ChangeType == "Changed")
            {
                tcs.TrySetResult(change);
            }
        };

        await tracker.StartAsync(CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task, "should receive changed event within timeout");

        await tracker.StopAsync(CancellationToken.None);

        tcs.Task.Result.PublishedFileId.Should().Be(id);
        tcs.Task.Result.ChangeType.Should().Be("Changed");
        tcs.Task.Result.Details.Should().Contain("Name");

        // Tracking DB should be updated with correct name
        var entry = _trackingDb.GetByPublishedFileId(id);
        entry.Should().NotBeNull();
        entry!.CachedName.Should().Be("NewName");
    }

    // ── OnTick: No changes ─────────────────────────────────────

    [Fact]
    public async Task OnTick_ShouldNotFireEvents_WhenNoChanges()
    {
        await _workshop.InitializeAsync();

        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Item",
            Description = "Desc",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });

        // Add matching tracking entry (same values)
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = id,
            CachedName = "Desc",
            CachedVisibility = WorkshopVisibility.Private,
            State = DownloadState.Downloaded,
            LastSyncedAt = DateTimeOffset.UtcNow
        });
        await _trackingDb.SaveAsync();

        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change => receivedEvents.Add(change);

        await tracker.StartAsync(CancellationToken.None);

        // Wait a few ticks
        await Task.Delay(100);

        await tracker.StopAsync(CancellationToken.None);

        receivedEvents.Should().BeEmpty("no changes should mean no events fired");
    }

    // ── StopAsync ──────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_ShouldStopTicking()
    {
        await _workshop.InitializeAsync();

        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change => receivedEvents.Add(change);

        await tracker.StartAsync(CancellationToken.None);
        await Task.Delay(50); // Let a tick or two happen
        await tracker.StopAsync(CancellationToken.None);

        var countAfterStop = receivedEvents.Count;

        // Wait more — no new events should arrive
        await Task.Delay(100);

        receivedEvents.Should().HaveCount(countAfterStop, "no events should fire after StopAsync");
    }

    [Fact]
    public async Task StopAsync_ShouldNotThrow_WhenNotStarted()
    {
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager);

        var act = () => tracker.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── Dispose ────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_ShouldNotThrow_WhenNotStarted()
    {
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager);

        var act = () => tracker.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_ShouldCleanup_AfterStartAndStop()
    {
        await _workshop.InitializeAsync();

        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));

        await tracker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await tracker.StopAsync(CancellationToken.None);

        var act = () => tracker.Dispose();
        act.Should().NotThrow();
    }

    // ── OnChange event null safety ─────────────────────────────

    [Fact]
    public async Task OnTick_ShouldNotThrow_WhenOnChangeIsNull()
    {
        await _workshop.InitializeAsync();

        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Item",
            Description = "Desc",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });

        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        // OnChange is not subscribed

        await tracker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await tracker.StopAsync(CancellationToken.None);

        // Should have updated tracking DB without throwing
        _trackingDb.GetAll().Should().NotBeEmpty();
    }

    // ── Rapid polling ───────────────────────────────────────────

    [Fact]
    public async Task OnTick_RapidPolling_ShouldNotDuplicateEvents()
    {
        await _workshop.InitializeAsync();

        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "RapidItem",
            Description = "RapidDesc",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });

        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(10));
        tracker.OnChange += change => receivedEvents.Add(change);

        await tracker.StartAsync(CancellationToken.None);

        // Wait for multiple rapid ticks
        await Task.Delay(200);

        await tracker.StopAsync(CancellationToken.None);

        // Should detect the new item exactly once, not on every tick
        var addedEvents = receivedEvents.Where(e => e.ChangeType == "Added").ToList();
        addedEvents.Should().HaveCount(1, "rapid polling should not duplicate detection events");
    }

    // ── Steam disconnected ──────────────────────────────────────

    [Fact]
    public async Task OnTick_ShouldNotFireEvents_WhenSteamDisconnects()
    {
        await _workshop.InitializeAsync();

        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "DisconnectItem",
            Description = "WillDisconnect",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });

        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change => receivedEvents.Add(change);

        await tracker.StartAsync(CancellationToken.None);
        await Task.Delay(60); // Let one tick run to detect the new item

        // Now disconnect Steam
        await _workshop.ShutdownAsync();
        _workshop.IsSteamRunning.Should().BeFalse();

        var countBeforeDisconnect = receivedEvents.Count;

        // Wait more ticks — no new events should fire since Steam is disconnected
        await Task.Delay(100);

        receivedEvents.Should().HaveCount(countBeforeDisconnect, "no events should fire when Steam is disconnected");
        await tracker.StopAsync(CancellationToken.None);
    }

    // ── Initial state: no items at all ──────────────────────────

    [Fact]
    public async Task OnTick_NoWorkshopItems_NoEventsFired()
    {
        await _workshop.InitializeAsync();

        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change => receivedEvents.Add(change);

        await tracker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await tracker.StopAsync(CancellationToken.None);

        receivedEvents.Should().BeEmpty();
    }

    // ── Many items at once ──────────────────────────────────────

    [Fact]
    public async Task OnTick_ManyNewItems_DetectsAll()
    {
        await _workshop.InitializeAsync();

        const int itemCount = 10;
        var createdIds = new List<ulong>();
        for (int i = 0; i < itemCount; i++)
        {
            var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
            {
                Title = $"BatchItem{i}",
                Description = $"BatchDesc{i}",
                MetadataJson = "{}",
                ContentPath = "dummy",
                Visibility = WorkshopVisibility.Private
            });
            createdIds.Add(id);
        }

        var tcs = new TaskCompletionSource<bool>();
        var receivedEvents = new List<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change =>
        {
            receivedEvents.Add(change);
            if (receivedEvents.Count(e => e.ChangeType == "Added") >= itemCount)
            {
                tcs.TrySetResult(true);
            }
        };

        await tracker.StartAsync(CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task, "should receive all added events within timeout");

        await tracker.StopAsync(CancellationToken.None);

        var addedEvents = receivedEvents.Where(e => e.ChangeType == "Added").ToList();
        addedEvents.Should().HaveCount(itemCount);
        createdIds.Should().AllSatisfy(id =>
            addedEvents.Should().Contain(e => e.PublishedFileId == id));
    }

    // ── Change type: both name and visibility ───────────────────

    [Fact]
    public async Task OnTick_ShouldDetectBothNameAndVisibilityChange()
    {
        await _workshop.InitializeAsync();

        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "BothTitle",
            Description = "BothDesc",
            MetadataJson = "{}",
            ContentPath = "dummy",
            Visibility = WorkshopVisibility.Private
        });

        // Track with different BOTH name AND visibility
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = id,
            CachedName = "OldName",
            CachedVisibility = WorkshopVisibility.Public,
            State = DownloadState.Downloaded
        });
        await _trackingDb.SaveAsync();

        var tcs = new TaskCompletionSource<TrackingChange>();
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));
        tracker.OnChange += change =>
        {
            if (change.ChangeType == "Changed")
            {
                tcs.TrySetResult(change);
            }
        };

        await tracker.StartAsync(CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);

        await tracker.StopAsync(CancellationToken.None);

        tcs.Task.Result.Details.Should().Contain("Visibility");
        tcs.Task.Result.Details.Should().Contain("Name");
    }

    // ── Dispose during active operation ─────────────────────────

    [Fact]
    public async Task Dispose_WhileRunning_StopsCleanly()
    {
        await _workshop.InitializeAsync();

        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager, TimeSpan.FromMilliseconds(20));

        await tracker.StartAsync(CancellationToken.None);
        await Task.Delay(30);

        // Dispose while running
        tracker.Dispose();
        // Should not throw
    }

    // ── Multiple stop calls ─────────────────────────────────────

    [Fact]
    public async Task StopAsync_MultipleCalls_DoesNotThrow()
    {
        var tracker = new AutoTracker(_workshop, _trackingDb, _fileGroupManager);

        await tracker.StartAsync(CancellationToken.None);
        await tracker.StopAsync(CancellationToken.None);

        var act = () => tracker.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
