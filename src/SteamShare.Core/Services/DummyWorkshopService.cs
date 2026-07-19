using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

/// <summary>
/// In-memory implementation of <see cref="IWorkshopService"/> for testing without Steam.
/// Activated when <c>STEAMSHARE_TEST_MODE=dummy</c>.
/// </summary>
internal sealed class DummyWorkshopService : IWorkshopService
{
    private readonly Dictionary<ulong, WorkshopItemInfo> _items = new();
    private readonly Dictionary<ulong, string> _installFolders = new();
    private readonly HashSet<ulong> _inProgressUploads = new();
    private ulong _nextId = 1;
    private bool _initialized;

    public bool IsSteamRunning => _initialized;
    public ulong CurrentSteamId => 76561198000000000;

    public Task<bool> InitializeAsync()
    {
        _initialized = true;

        // Pre-populate with sample data for CLI integration tests
        if (Environment.GetEnvironmentVariable("STEAMSHARE_TEST_PREPOPULATE") == "1")
        {
            PrePopulateSampleData();
        }

        return Task.FromResult(true);
    }

    private void PrePopulateSampleData(int itemCount = 3)
    {
        for (int i = 0; i < itemCount; i++)
        {
            var id = _nextId++;
            var metadata = new FileGroupMetadata
            {
                Name = $"Test File Group {i + 1}",
                FileCount = i + 1,
                TotalSizeBytes = (i + 1) * 1024,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _items[id] = new WorkshopItemInfo
            {
                PublishedFileId = id,
                Title = $"[SS]Test File Group {i + 1}",
                Description = $"Sample file group #{i + 1} for testing",
                MetadataJson = metadata.ToJson(),
                Visibility = i == 2 ? WorkshopVisibility.Public : WorkshopVisibility.Private,
                OwnerSteamId = CurrentSteamId,
                Subscriptions = (uint)(i * 10),
                Favorites = (uint)i,
                DownloadCount = (uint)(i * 10),
                KeyValueTags = new Dictionary<string, string>
                {
                    ["steamshare_tag"] = "true"
                }
            };
        }
    }

    public Task ShutdownAsync()
    {
        _initialized = false;
        return Task.CompletedTask;
    }

    public Task<ulong> CreateItemAsync(WorkshopItemCreateRequest request,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        var id = _nextId++;
        _inProgressUploads.Add(id);
        _items[id] = new WorkshopItemInfo
        {
            PublishedFileId = id,
            Title = request.Title,
            Description = request.Description,
            MetadataJson = request.MetadataJson,
            Visibility = request.Visibility,
            OwnerSteamId = CurrentSteamId,
            Subscriptions = 0,
            Favorites = 0,
            DownloadCount = 0,
            KeyValueTags = request.KeyValueTags
        };

        if (!string.IsNullOrEmpty(request.ContentPath))
        {
            _installFolders[id] = request.ContentPath;
        }

        // Simulate upload completing after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(500, ct);
            _inProgressUploads.Remove(id);
        }, ct);

        return Task.FromResult(id);
    }

    public Task UpdateItemAsync(WorkshopItemUpdateRequest request,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        if (!_items.TryGetValue(request.PublishedFileId, out var info))
        {
            return Task.CompletedTask;
        }

        _inProgressUploads.Add(request.PublishedFileId);

        _items[request.PublishedFileId] = info with
        {
            Title = request.Title ?? info.Title,
            Description = request.Description ?? info.Description,
            MetadataJson = request.MetadataJson ?? info.MetadataJson,
            Visibility = request.Visibility ?? info.Visibility
        };

        if (request.ContentPath != null)
        {
            _installFolders[request.PublishedFileId] = request.ContentPath;
        }

        // Simulate upload completing after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(500, ct);
            _inProgressUploads.Remove(request.PublishedFileId);
        }, ct);

        return Task.CompletedTask;
    }

    public Task<WorkshopItemDownloadResult> DownloadItemAsync(ulong publishedFileId,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        var targetDirectory = Path.GetTempFileName() + ".dir";
        Directory.CreateDirectory(targetDirectory);
        var dummyFile = Path.Combine(targetDirectory, "steamshare_filegroup_content.dat");
        File.WriteAllText(dummyFile, $"Dummy content for item {publishedFileId}");

        return Task.FromResult(new WorkshopItemDownloadResult
        {
            PublishedFileId = publishedFileId,
            LocalFolderPath = targetDirectory,
            SizeOnDiskBytes = (ulong)new FileInfo(dummyFile).Length
        });
    }

    public Task<IReadOnlyList<WorkshopItemInfo>> QueryOwnedItemsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<WorkshopItemInfo>>(
            _items.Values.Where(i => i.OwnerSteamId == CurrentSteamId).ToList().AsReadOnly());
    }

    public Task<WorkshopItemInfo?> QueryItemByIdAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        _items.TryGetValue(publishedFileId, out var info);
        return Task.FromResult(info);
    }

    public Task DeleteItemAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        _items.Remove(publishedFileId);
        _installFolders.Remove(publishedFileId);
        return Task.CompletedTask;
    }

    public Task SetItemVisibilityAsync(ulong publishedFileId, WorkshopVisibility visibility, CancellationToken ct = default)
    {
        if (_items.TryGetValue(publishedFileId, out var info))
        {
            _items[publishedFileId] = info with { Visibility = visibility };
        }

        return Task.CompletedTask;
    }

    public Task<WorkshopItemDownloadResult?> GetItemInstallInfoAsync(ulong publishedFileId)
    {
        if (!_installFolders.TryGetValue(publishedFileId, out var folder))
        {
            return Task.FromResult<WorkshopItemDownloadResult?>(null);
        }

        return Task.FromResult<WorkshopItemDownloadResult?>(new WorkshopItemDownloadResult
        {
            PublishedFileId = publishedFileId,
            LocalFolderPath = folder,
            SizeOnDiskBytes = (ulong)(Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles().Sum(f => f.Length)
                : 0)
        });
    }

    public Task<(ulong ProcessedBytes, ulong TotalBytes)> GetItemUploadProgressAsync(ulong publishedFileId)
    {
        if (_inProgressUploads.Contains(publishedFileId))
        {
            return Task.FromResult((0UL, 100UL));
        }

        if (_items.ContainsKey(publishedFileId))
        {
            return Task.FromResult((100UL, 100UL));
        }

        return Task.FromResult((0UL, 0UL));
    }

    public Task<(ulong ProcessedBytes, ulong TotalBytes)> GetItemDownloadProgressAsync(ulong publishedFileId)
    {
        if (_items.ContainsKey(publishedFileId))
        {
            return Task.FromResult((100UL, 100UL));
        }

        return Task.FromResult((0UL, 0UL));
    }
}
