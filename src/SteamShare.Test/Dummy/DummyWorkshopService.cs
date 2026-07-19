using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;
using SteamShare.Core.Services;

namespace SteamShare.Test.Dummy;

/// <summary>
/// In-memory implementation of IWorkshopService for CI testing.
/// Simulates Steam Workshop operations without requiring a real Steam client.
/// Activate by setting environment variable STEAMSHARE_TEST_MODE=dummy.
/// </summary>
public class DummyWorkshopService : IWorkshopService
{
    private readonly Dictionary<ulong, WorkshopItemInfo> _items = new();
    private readonly Dictionary<ulong, string> _installFolders = new();
    private readonly HashSet<ulong> _inProgressUploads = new();
    private ulong _nextId = 1;
    private bool _initialized;

    public bool IsSteamRunning => _initialized;
    public ulong CurrentSteamId => 76561198000000000; // Fake Steam ID

    public Task<bool> InitializeAsync()
    {
        _initialized = true;
        return Task.FromResult(true);
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
        ct.ThrowIfCancellationRequested();

        var id = _nextId++;
        _inProgressUploads.Add(id);
        var info = new WorkshopItemInfo
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
        _items[id] = info;

        // Simulate content path
        if (!string.IsNullOrEmpty(request.ContentPath))
        {
            _installFolders[id] = request.ContentPath;
        }

        // Simulate upload completing after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            _inProgressUploads.Remove(id);
        }, ct);

        return Task.FromResult(id);
    }

    public Task UpdateItemAsync(WorkshopItemUpdateRequest request,
    Action<long, long>? progressCallback,
    CancellationToken ct = default)
    {
        if (!_items.TryGetValue(request.PublishedFileId, out var info))
        {
            throw new FileGroupNotFoundException(request.PublishedFileId);
        }

        _inProgressUploads.Add(request.PublishedFileId);

        var updated = info with
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

        _items[request.PublishedFileId] = updated;

        // Simulate upload completing after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            _inProgressUploads.Remove(request.PublishedFileId);
        }, ct);

        return Task.CompletedTask;
    }

    public Task<WorkshopItemDownloadResult> DownloadItemAsync(ulong publishedFileId,
        Action<long, long>? progressCallback,
        CancellationToken ct = default)
    {
        var targetDirectory = Path.GetTempFileName() + ".dir";
        ct.ThrowIfCancellationRequested();

        if (!_items.ContainsKey(publishedFileId))
        {
            throw new FileGroupNotFoundException(publishedFileId);
        }

        Directory.CreateDirectory(targetDirectory);

        // Copy the actual uploaded content if available, otherwise create dummy
        if (_installFolders.TryGetValue(publishedFileId, out var sourcePath))
        {
            if (Directory.Exists(sourcePath))
            {
                // Folder-based file group: copy all files including filegroup_sha.json
                foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourcePath, file);
                    var destPath = Path.Combine(targetDirectory, relativePath);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    File.Copy(file, destPath, overwrite: true);
                }
                var totalSize = Directory.GetFiles(targetDirectory, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                return Task.FromResult(new WorkshopItemDownloadResult
                {
                    PublishedFileId = publishedFileId,
                    LocalFolderPath = targetDirectory,
                    SizeOnDiskBytes = (ulong)totalSize
                });
            }

            if (File.Exists(sourcePath))
            {
                // Legacy ZIP-based: copy single file
                var destFile = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destFile, overwrite: true);
                var fileInfo = new FileInfo(destFile);
                return Task.FromResult(new WorkshopItemDownloadResult
                {
                    PublishedFileId = publishedFileId,
                    LocalFolderPath = targetDirectory,
                    SizeOnDiskBytes = (ulong)fileInfo.Length
                });
            }
        }

        // Fallback: create dummy file
        var dummyFile = Path.Combine(targetDirectory, "dummy_content.txt");
        File.WriteAllText(dummyFile, $"Dummy content for item {publishedFileId}");

        var dummyResult = new WorkshopItemDownloadResult
        {
            PublishedFileId = publishedFileId,
            LocalFolderPath = targetDirectory,
            SizeOnDiskBytes = (ulong)new FileInfo(dummyFile).Length
        };

        return Task.FromResult(dummyResult);
    }

    public Task<IReadOnlyList<WorkshopItemInfo>> QueryOwnedItemsAsync(CancellationToken ct = default)
    {
        var owned = _items.Values
            .Where(i => i.OwnerSteamId == CurrentSteamId)
            .ToList()
            .AsReadOnly();
        return Task.FromResult<IReadOnlyList<WorkshopItemInfo>>(owned);
    }

    public Task<WorkshopItemInfo?> QueryItemByIdAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        _items.TryGetValue(publishedFileId, out var info);
        return Task.FromResult(info);
    }

    public Task DeleteItemAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        _items.Remove(publishedFileId);
        _installFolders.Remove(publishedFileId);
        return Task.CompletedTask;
    }

    public Task SetItemVisibilityAsync(ulong publishedFileId, WorkshopVisibility visibility, CancellationToken ct = default)
    {
        if (!_items.TryGetValue(publishedFileId, out var info))
        {
            throw new FileGroupNotFoundException(publishedFileId);
        }

        _items[publishedFileId] = info with { Visibility = visibility };
        return Task.CompletedTask;
    }

    public Task<WorkshopItemDownloadResult?> GetItemInstallInfoAsync(ulong publishedFileId)
    {
        if (!_installFolders.TryGetValue(publishedFileId, out var folder))
        {
            return Task.FromResult<WorkshopItemDownloadResult?>(null);
        }

        var result = new WorkshopItemDownloadResult
        {
            PublishedFileId = publishedFileId,
            LocalFolderPath = folder,
            SizeOnDiskBytes = (ulong)(Directory.Exists(folder) ? new DirectoryInfo(folder).GetFiles().Sum(f => f.Length) : 0)
        };

        return Task.FromResult<WorkshopItemDownloadResult?>(result);
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

    /// <summary>
    /// Replaces the stored metadata JSON for a workshop item.
    /// Useful for testing hash mismatch scenarios.
    /// </summary>
    public void TamperMetadata(ulong publishedFileId, string newMetadataJson)
    {
        if (_items.TryGetValue(publishedFileId, out var info))
        {
            _items[publishedFileId] = info with { MetadataJson = newMetadataJson };
        }
    }

    /// <summary>
    /// Creates a dummy service pre-populated with sample data for testing.
    /// </summary>
    public static DummyWorkshopService CreateWithSampleData(int itemCount = 3)
    {
        var service = new DummyWorkshopService();
        service._initialized = true;

        for (int i = 0; i < itemCount; i++)
        {
            var id = service._nextId++;
            service._items[id] = new WorkshopItemInfo
            {
                PublishedFileId = id,
                Title = $"Test File Group {i + 1}",
                Description = $"Sample file group #{i + 1} for testing",
                MetadataJson = new FileGroupMetadata { Name = $"Test {i + 1}", FileCount = i + 1, TotalSizeBytes = (i + 1) * 1024, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
                Visibility = i == 2 ? WorkshopVisibility.Public : WorkshopVisibility.Private,
                OwnerSteamId = service.CurrentSteamId,
                Subscriptions = (uint)(i * 10),
                Favorites = (uint)i,
                DownloadCount = (uint)(i * 10),
                KeyValueTags = new Dictionary<string, string>
                {
                    ["steamshare_tag"] = "true"
                }
            };
        }

        return service;
    }
}
