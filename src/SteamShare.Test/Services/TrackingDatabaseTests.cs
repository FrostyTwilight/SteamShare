using SteamShare.Core.Models;
using SteamShare.Core.Services;

namespace SteamShare.Test.Services;

public class TrackingDatabaseTests : IDisposable
{
    private readonly string _testDir;
    private TrackingDatabaseService _service;

    public TrackingDatabaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SteamShareTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _service = new TrackingDatabaseService(_testDir);
    }

    [Fact]
    public void Constructor_LoadsEmptyDatabase_WhenNoFileExists()
    {
        _service.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Upsert_AddsEntry_AndGetById_ReturnsIt()
    {
        var entry = new FileGroupTrackingEntry
        {
            PublishedFileId = 12345,
            CachedName = "Test",
            State = DownloadState.Downloaded
        };
        _service.Upsert(entry);

        var retrieved = _service.GetByPublishedFileId(12345);
        retrieved.Should().NotBeNull();
        retrieved!.CachedName.Should().Be("Test");
        retrieved.State.Should().Be(DownloadState.Downloaded);
    }

    [Fact]
    public void Upsert_UpdatesExistingEntry()
    {
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1, CachedName = "Old" });
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1, CachedName = "New" });

        _service.GetByPublishedFileId(1)!.CachedName.Should().Be("New");
    }

    [Fact]
    public void Remove_ReturnsFalse_WhenEntryNotFound()
    {
        _service.Remove(99999).Should().BeFalse();
    }

    [Fact]
    public void Remove_ReturnsTrue_AndGetByIdReturnsNull()
    {
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1 });
        _service.Remove(1).Should().BeTrue();
        _service.GetByPublishedFileId(1).Should().BeNull();
    }

    [Fact]
    public void GetByState_ReturnsOnlyMatchingEntries()
    {
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1, State = DownloadState.Downloaded });
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 2, State = DownloadState.Downloading });
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 3, State = DownloadState.Downloaded });

        var downloaded = _service.GetByState(DownloadState.Downloaded);
        downloaded.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_PersistsToDisk()
    {
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1, CachedName = "Saved" });
        await _service.SaveAsync();

        // Reload from disk
        var newService = new TrackingDatabaseService(_testDir);
        var entry = newService.GetByPublishedFileId(1);
        entry.Should().NotBeNull();
        entry!.CachedName.Should().Be("Saved");
    }

    // ── Large entries ───────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_LargeNumberOfEntries_PersistsAndReloads()
    {
        const int entryCount = 500;
        for (ulong i = 1; i <= entryCount; i++)
        {
            _service.Upsert(new FileGroupTrackingEntry
            {
                PublishedFileId = i,
                CachedName = $"Entry_{i}",
                State = i % 3 == 0 ? DownloadState.Downloaded : DownloadState.NotDownloaded,
                CachedVisibility = i % 2 == 0 ? WorkshopVisibility.Public : WorkshopVisibility.Private,
                LastSyncedAt = DateTimeOffset.UtcNow
            });
        }

        await _service.SaveAsync();

        var newService = new TrackingDatabaseService(_testDir);
        var all = newService.GetAll();
        all.Should().HaveCount(entryCount);
        all.Should().Contain(e => e.PublishedFileId == 1);
        all.Should().Contain(e => e.PublishedFileId == (ulong)entryCount);
    }

    // ── Concurrent access ───────────────────────────────────────

    [Fact]
    public void Upsert_ConcurrentAccess_IsThreadSafe()
    {
        const int count = 100;
        var tasks = new Task[count];

        for (int i = 0; i < count; i++)
        {
            var id = (ulong)(i + 1);
            tasks[i] = Task.Run(() =>
            {
                _service.Upsert(new FileGroupTrackingEntry
                {
                    PublishedFileId = id,
                    CachedName = $"Concurrent_{id}"
                });
            });
        }

        Task.WaitAll(tasks);

        var all = _service.GetAll();
        all.Should().HaveCount(count);
    }

    [Fact]
    public async Task SaveAsync_ConcurrentSaveAndUpsert_DoesNotCorrupt()
    {
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1, CachedName = "Initial" });

        var saveTask = _service.SaveAsync();
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 2, CachedName = "During" });
        await saveTask;

        // Entry 2 may or may not be in the saved file, but the service should not throw
        var newService = new TrackingDatabaseService(_testDir);
        var all = newService.GetAll();
        all.Should().NotBeNull();
    }

    // ── Persistence: file round-trip ────────────────────────────

    [Fact]
    public async Task SaveAsync_RoundTrip_AllFieldsPreserved()
    {
        var original = new FileGroupTrackingEntry
        {
            PublishedFileId = 42,
            LocalPath = "/path/to/file.zip",
            State = DownloadState.Downloaded,
            CachedName = "RoundTrip",
            CachedVisibility = WorkshopVisibility.Unlisted,
            LastSyncedAt = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero)
        };
        _service.Upsert(original);
        await _service.SaveAsync();

        var newService = new TrackingDatabaseService(_testDir);
        var loaded = newService.GetByPublishedFileId(42);
        loaded.Should().NotBeNull();
        loaded!.PublishedFileId.Should().Be(42);
        loaded.LocalPath.Should().Be("/path/to/file.zip");
        loaded.State.Should().Be(DownloadState.Downloaded);
        loaded.CachedName.Should().Be("RoundTrip");
        loaded.CachedVisibility.Should().Be(WorkshopVisibility.Unlisted);
        loaded.LastSyncedAt.Should().Be(new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero));
    }

    // ── Remove edge cases ───────────────────────────────────────

    [Fact]
    public void Remove_AllEntries_GetAllReturnsEmpty()
    {
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1 });
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 2 });
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 3 });

        _service.Remove(1);
        _service.Remove(2);
        _service.Remove(3);

        _service.GetAll().Should().BeEmpty();
    }

    // ── Legacy JSON migration ────────────────────────────────────

    [Fact]
    public void Constructor_MigratesFromLegacyJson()
    {
        // Write a legacy filegroups.json with sample data
        var jsonPath = Path.Combine(_testDir, "filegroups.json");
        var json = """
        {
            "Entries": [
                {
                    "PublishedFileId": 100,
                    "LocalPath": "/test/path.zip",
                    "State": 2,
                    "CachedName": "MigratedEntry",
                    "CachedVisibility": 1,
                    "LastSyncedAt": "2025-06-15T12:00:00.0000000+00:00"
                },
                {
                    "PublishedFileId": 200,
                    "LocalPath": null,
                    "State": 0,
                    "CachedName": "AnotherEntry",
                    "CachedVisibility": 0,
                    "LastSyncedAt": null
                }
            ]
        }
        """;
        File.WriteAllText(jsonPath, json);

        // Create new service on same directory — migration should happen
        _service = new TrackingDatabaseService(_testDir);

        // Verify data was migrated
        var all = _service.GetAll();
        all.Should().HaveCount(2);

        var entry1 = _service.GetByPublishedFileId(100);
        entry1.Should().NotBeNull();
        entry1!.LocalPath.Should().Be("/test/path.zip");
        entry1.CachedName.Should().Be("MigratedEntry");
        entry1.CachedVisibility.Should().Be(WorkshopVisibility.Unlisted);
        entry1.LastSyncedAt.Should().Be(new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero));

        var entry2 = _service.GetByPublishedFileId(200);
        entry2.Should().NotBeNull();
        entry2!.CachedName.Should().Be("AnotherEntry");
        entry2.State.Should().Be(DownloadState.NotDownloaded);

        // Verify JSON was renamed to .bak
        File.Exists(jsonPath).Should().BeFalse();
        File.Exists(jsonPath + ".bak").Should().BeTrue();
    }

    // ── Corrupted database handling ───────────────────────────────

    [Fact]
    public void Constructor_CorruptedDatabaseFile_StartsFresh()
    {
        var dbPath = Path.Combine(_testDir, "steamshare.db");
        // Write random bytes instead of valid SQLite database
        File.WriteAllText(dbPath, "{ this is not a valid sqlite database @@@");

        // Should not throw — SQLite will delete and recreate the database
        _service = new TrackingDatabaseService(_testDir);
        _service.GetAll().Should().BeEmpty();
    }

    // ── Empty string data directory ─────────────────────────────

    [Fact]
    public void Constructor_EmptyDataDirectory_ThrowsArgumentException()
    {
        var act = () => new TrackingDatabaseService("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhitespaceDataDirectory_ThrowsArgumentException()
    {
        var act = () => new TrackingDatabaseService("   ");

        act.Should().Throw<ArgumentException>();
    }

    // ── Save with CancellationToken ─────────────────────────────

    [Fact]
    public async Task SaveAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _service.SaveAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── GetByState with no matches ──────────────────────────────

    [Fact]
    public void GetByState_NoMatches_ReturnsEmptyList()
    {
        _service.Upsert(new FileGroupTrackingEntry { PublishedFileId = 1, State = DownloadState.NotDownloaded });

        var result = _service.GetByState(DownloadState.Downloading);

        result.Should().BeEmpty();
    }

    // ── Pending tasks ─────────────────────────────────────────────

    [Fact]
    public void SavePendingTask_ThenGetPendingTasks_ReturnsTask()
    {
        _service.SavePendingTask(
            taskType: 0,
            publishedFileId: 12345,
            shareKey: "sshare+abc",
            password: null,
            sourcePath: null,
            targetPath: "/downloads/test.zip",
            name: "Test Group",
            virtualFolderPath: null,
            visibility: 0);

        var tasks = _service.GetPendingTasks();
        tasks.Should().HaveCount(1);
        tasks[0].TaskType.Should().Be(0);
        tasks[0].PublishedFileId.Should().Be(12345);
        tasks[0].ShareKey.Should().Be("sshare+abc");
        tasks[0].TargetPath.Should().Be("/downloads/test.zip");
        tasks[0].Name.Should().Be("Test Group");
    }

    [Fact]
    public void DeletePendingTask_RemovesTask()
    {
        _service.SavePendingTask(
            taskType: 1,
            publishedFileId: null,
            shareKey: null,
            password: null,
            sourcePath: "/uploads/test.zip",
            targetPath: null,
            name: "Upload Task",
            virtualFolderPath: "/vfolder",
            visibility: 1);

        var tasks = _service.GetPendingTasks();
        tasks.Should().HaveCount(1);

        _service.DeletePendingTask(tasks[0].Id);
        _service.GetPendingTasks().Should().BeEmpty();
    }

    [Fact]
    public void DeletePendingTasksByFileId_RemovesMatchingTasks()
    {
        _service.SavePendingTask(
            taskType: 0, publishedFileId: 100,
            shareKey: "k1", password: null, sourcePath: null,
            targetPath: "/t1", name: "A", virtualFolderPath: null, visibility: 0);

        _service.SavePendingTask(
            taskType: 0, publishedFileId: 100,
            shareKey: "k2", password: null, sourcePath: null,
            targetPath: "/t2", name: "B", virtualFolderPath: null, visibility: 0);

        _service.SavePendingTask(
            taskType: 0, publishedFileId: 200,
            shareKey: "k3", password: null, sourcePath: null,
            targetPath: "/t3", name: "C", virtualFolderPath: null, visibility: 0);

        _service.DeletePendingTasksByFileId(100);

        var remaining = _service.GetPendingTasks();
        remaining.Should().HaveCount(1);
        remaining[0].PublishedFileId.Should().Be(200);
    }

    [Fact]
    public void SavePendingTask_WithTaskId_PersistsAndReturnsTaskId()
    {
        _service.SavePendingTask(
            taskType: 0,
            publishedFileId: 12345,
            shareKey: "sshare+abc",
            password: null,
            sourcePath: null,
            targetPath: "/downloads/test.zip",
            name: "Test Group",
            virtualFolderPath: null,
            visibility: 0,
            taskId: "abc123def456");

        var tasks = _service.GetPendingTasks();
        tasks.Should().HaveCount(1);
        tasks[0].TaskId.Should().Be("abc123def456");
    }

    [Fact]
    public void GetPendingTasks_Empty_ReturnsEmptyList()
    {
        var tasks = _service.GetPendingTasks();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public void DeleteDuplicatePendingTasks_RemovesOlderDuplicates()
    {
        // Save 3 download tasks with the same shareKey — only newest (highest id) survives.
        _service.SavePendingTask(
            taskType: 0, publishedFileId: 100,
            shareKey: "sshare+dup", password: null, sourcePath: null,
            targetPath: "/dl/old", name: "OldDL", virtualFolderPath: null, visibility: 0);
        _service.SavePendingTask(
            taskType: 0, publishedFileId: 100,
            shareKey: "sshare+dup", password: null, sourcePath: null,
            targetPath: "/dl/mid", name: "MidDL", virtualFolderPath: null, visibility: 0);
        _service.SavePendingTask(
            taskType: 0, publishedFileId: 100,
            shareKey: "sshare+dup", password: null, sourcePath: null,
            targetPath: "/dl/new", name: "NewDL", virtualFolderPath: null, visibility: 0);

        // Save 2 upload tasks with the same sourcePath+name — only newest survives.
        _service.SavePendingTask(
            taskType: 1, publishedFileId: null,
            shareKey: null, password: null, sourcePath: "/src/test",
            targetPath: null, name: "UploadDup", virtualFolderPath: "/vf", visibility: 1);
        _service.SavePendingTask(
            taskType: 1, publishedFileId: null,
            shareKey: null, password: null, sourcePath: "/src/test",
            targetPath: null, name: "UploadDup", virtualFolderPath: "/vf", visibility: 1);

        _service.DeleteDuplicatePendingTasks();

        var remaining = _service.GetPendingTasks();
        remaining.Should().HaveCount(2);
        remaining.Should().ContainSingle(p => p.TaskType == 0);
        remaining.Should().ContainSingle(p => p.TaskType == 1);

        // The surviving download should be the one with highest id (last inserted).
        var dl = remaining.Single(p => p.TaskType == 0);
        dl.TargetPath.Should().Be("/dl/new");

        // The surviving upload should be the last inserted.
        var ul = remaining.Single(p => p.TaskType == 1);
        ul.Id.Should().BeGreaterThan(dl.Id); // uploads inserted after downloads
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }
}
