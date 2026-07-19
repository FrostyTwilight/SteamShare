using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Serilog;

using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;
using SteamShare.Core.Utilities;

using Steamworks;

namespace SteamShare.Core.Services;

/// <summary>
/// Manages file group lifecycle: creates file group directories with SHA-256
/// manifests, verifies integrity via filegroup_sha.json, and performs Steam
/// Workshop upload/download/delete operations.
/// </summary>
public class FileGroupManager
{
    private static readonly ILogger LogSerilog = Log.ForContext<FileGroupManager>();
    private readonly IWorkshopService _workshop;
    private readonly TrackingDatabaseService _trackingDb;

    /// <summary>
    /// Initializes a new instance of <see cref="FileGroupManager"/>.
    /// </summary>
    /// <param name="workshop">Steam Workshop service for upload/download.</param>
    /// <param name="trackingDb">Tracking database for local persistence.</param>
    public FileGroupManager(IWorkshopService workshop, TrackingDatabaseService trackingDb)
    {
        _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
        _trackingDb = trackingDb ?? throw new ArgumentNullException(nameof(trackingDb));
    }

    /// <summary>
    /// Computes a workshop item title from a file group name.
    /// </summary>
    private static string ComputeWorkshopTitle(string name)
    {
        return "[SS]" + name;
    }

    // ── Manifest operations ─────────────────────────────────────────────

    /// <summary>
    /// Enumerates all files recursively in <paramref name="directory"/>,
    /// computes SHA-256 for each, writes a <c>filegroup_sha.json</c> manifest
    /// to the directory root, and returns the SHA-256 hash of the manifest
    /// file itself.
    /// </summary>
    /// <param name="directory">Path to the directory containing the file group.</param>
    /// <returns>SHA-256 hash (lowercase hex) of the filegroup_sha.json manifest.</returns>
    public static async Task<string> ComputeFileGroupShaAsync(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        // Enumerate all files, compute individual SHA-256 hashes
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        var fileHashes = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        Task[] tasks = [
            ..files.Select(async file => {
                await Task.Delay(1).ConfigureAwait(false); // Yield to avoid blocking
                var relativePath = Path.GetRelativePath(directory, file);
                var hash = await ComputeFileHashAsync(file);
                fileHashes[relativePath] = hash;
            })
            ];

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Build manifest object and serialize to JSON
        var manifest = new Dictionary<string, object>
        {
            ["files"] = fileHashes,
            ["total_files"] = fileHashes.Count
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var manifestPath = Path.Combine(directory, "filegroup_sha.json");
        var manifestBytes = Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(manifestPath, manifestBytes);

        // Compute hash of the manifest itself
        var manifestHashBytes = SHA256.HashData(manifestBytes);
        return Convert.ToHexStringLower(manifestHashBytes);
    }

    /// <summary>
    /// Verifies that the filegroup_sha.json manifest hash matches
    /// <paramref name="expectedManifestHash"/>, then verifies every file
    /// listed in the manifest has the correct SHA-256 hash.
    /// </summary>
    /// <param name="directory">Path to the directory containing the file group.</param>
    /// <param name="expectedManifestHash">Expected SHA-256 hash (lowercase hex) of filegroup_sha.json.</param>
    /// <exception cref="InvalidOperationException">Thrown when the manifest hash or any file hash does not match.</exception>
    public static async Task VerifyFileGroupShaAsync(string directory, string expectedManifestHash)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(expectedManifestHash);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        var manifestPath = Path.Combine(directory, "filegroup_sha.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Manifest not found: {manifestPath}");
        }

        // Step 1: Verify manifest hash
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath).ConfigureAwait(false);
        var actualManifestHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));

        if (!string.Equals(actualManifestHash, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Manifest hash mismatch: expected {expectedManifestHash}, got {actualManifestHash}");
        }

        // Step 2: Parse manifest and verify each file
        var manifestJson = Encoding.UTF8.GetString(manifestBytes);
        using var doc = JsonDocument.Parse(manifestJson);

        if (!doc.RootElement.TryGetProperty("files", out var filesElement))
        {
            throw new InvalidOperationException("Manifest is missing 'files' property");
        }

        var errors = new ConcurrentBag<Exception>();
        var maxConcurrency = Math.Min(Environment.ProcessorCount, 8);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency
        };

        await Parallel.ForEachAsync(filesElement.EnumerateObject(), parallelOptions, async (fileEntry, _) =>
        {
            var relativePath = fileEntry.Name;
            var expectedFileHash = fileEntry.Value.GetString();
            if (expectedFileHash is null)
            {
                errors.Add(new InvalidOperationException($"Missing hash for file: {relativePath}"));
                return;
            }

            var filePath = Path.Combine(directory, relativePath);
            if (!File.Exists(filePath))
            {
                errors.Add(new FileNotFoundException($"File listed in manifest not found: {filePath}"));
                return;
            }

            var actualFileHash = await ComputeFileHashAsync(filePath).ConfigureAwait(false);
            if (!string.Equals(actualFileHash, expectedFileHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new InvalidOperationException(
                    $"File hash mismatch for '{relativePath}': expected {expectedFileHash}, got {actualFileHash}"));
            }
        }).ConfigureAwait(false);

        if (!errors.IsEmpty)
        {
            throw new AggregateException(errors);
        }

        LogSerilog.Debug("Integrity verified for {Directory}: manifest hash matches", directory);
    }

    // ── Create ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a file group from a source directory, computes per-file SHA-256
    /// hashes into a filegroup_sha.json manifest, creates a Steam Workshop item
    /// with the directory as content, and returns a fully populated
    /// <see cref="FileGroup"/> record.
    /// </summary>
    /// <param name="sourceDir">Path to the source directory to publish.</param>
    /// <param name="name">Human-readable name for the file group.</param>
    /// <param name="virtualFolderPath">Virtual folder path within the file group tree. Empty for root.</param>
    /// <returns>A populated <see cref="FileGroup"/> with the local folder path and manifest hash.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="sourceDir"/> does not exist.</exception>
    public async Task<FileGroup> CreateFromDirectoryAsync(
        string sourceDir, string name, string virtualFolderPath = "")
    {
        ArgumentNullException.ThrowIfNull(sourceDir);
        ArgumentNullException.ThrowIfNull(name);

        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        LogSerilog.Information("Creating file group from {SourceDir} (name={Name})", sourceDir, name);

        // Enumerate files to capture metadata
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        var fileCount = files.Length;
        var totalSizeBytes = files.Sum(f => new FileInfo(f).Length);

        LogSerilog.Debug("Found {FileCount} files ({TotalSize} bytes) in {SourceDir}",
            fileCount, totalSizeBytes, sourceDir);

        // Compute filegroup_sha.json manifest and get its hash
        var manifestHash = await ComputeFileGroupShaAsync(sourceDir);
        LogSerilog.Debug("Manifest hash: {Hash}", manifestHash);

        // Create workshop item with directory content and metadata
        var metadataObj = new FileGroupMetadata
        {
            Name = name,
            VirtualFolderPath = virtualFolderPath,
            ManifestHash = manifestHash,
            FileCount = fileCount,
            TotalSizeBytes = totalSizeBytes,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = "0.1.0",
            GitCommit = "50881a1"
        };

        var title = ComputeWorkshopTitle(name);
        LogSerilog.Debug("Creating workshop item: title={Title}", title);

        var publishedFileId = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = title,
            Description = name,
            MetadataJson = metadataObj.ToJson(),
            ContentPath = sourceDir,
            Visibility = WorkshopVisibility.Private,
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        }, null);

        LogSerilog.Debug("Workshop item created: {PublishedFileId}", publishedFileId);

        // Persist tracking entry
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = publishedFileId,
            LocalPath = sourceDir,
            State = DownloadState.NotDownloaded,
            CachedName = name,
            CachedVisibility = WorkshopVisibility.Private,
            LastSyncedAt = DateTimeOffset.UtcNow
        });
        await _trackingDb.SaveAsync();

        return new FileGroup
        {
            Name = name,
            VirtualFolderPath = virtualFolderPath,
            LocalFolderPath = sourceDir,
            ManifestHash = manifestHash,
            FileCount = fileCount,
            TotalSizeBytes = totalSizeBytes,
            CreatedAt = DateTimeOffset.UtcNow,
            PublishedFileId = publishedFileId,
            OwnerSteamId = _workshop.CurrentSteamId,
            Visibility = WorkshopVisibility.Private
        };
    }

    // ── Upload ───────────────────────────────────────────────────────────

    /// <summary>
    /// Uploads the directory content of a file group to an already-created workshop item.
    /// The workshop item must have been created by <see cref="CreateFromDirectoryAsync"/> first.
    /// </summary>
    public async Task<FileGroup> UploadAsync(FileGroup fileGroup, WorkshopVisibility? visibility = null,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileGroup);

        if (string.IsNullOrEmpty(fileGroup.LocalFolderPath) || !Directory.Exists(fileGroup.LocalFolderPath))
        {
            throw new InvalidOperationException(
                "File group must have a valid LocalFolderPath pointing to an existing directory.");
        }

        if (fileGroup.PublishedFileId == 0)
        {
            throw new InvalidOperationException(
                "File group must have a PublishedFileId. CreateFromDirectoryAsync creates the workshop item first.");
        }

        LogSerilog.Information("Uploading content for {Name} (PublishedFileId={Id})",
            fileGroup.Name, fileGroup.PublishedFileId);

        // Submit content (directory path — Steamworks.NET SetItemContent accepts directories)
        LogSerilog.Debug("Submitting content for {PublishedFileId}", fileGroup.PublishedFileId);
        await _workshop.UpdateItemAsync(new WorkshopItemUpdateRequest
        {
            PublishedFileId = fileGroup.PublishedFileId,
            ContentPath = fileGroup.LocalFolderPath
        }, progressCallback, ct).ConfigureAwait(false);

        // Optionally update visibility
        if (visibility is not null && visibility != fileGroup.Visibility)
        {
            await _workshop.SetItemVisibilityAsync(fileGroup.PublishedFileId, visibility.Value, ct).ConfigureAwait(false);
        }

        // Update tracking
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = fileGroup.PublishedFileId,
            LocalPath = fileGroup.LocalFolderPath,
            State = DownloadState.NotDownloaded,
            CachedName = fileGroup.Name,
            CachedVisibility = visibility ?? fileGroup.Visibility,
            LastSyncedAt = DateTimeOffset.UtcNow
        });
        await _trackingDb.SaveAsync(ct).ConfigureAwait(false);

        LogSerilog.Information("Upload completed for {Name} (PublishedFileId={Id})",
            fileGroup.Name, fileGroup.PublishedFileId);

        return fileGroup with { UpdatedAt = DateTimeOffset.UtcNow, Visibility = visibility ?? fileGroup.Visibility };
    }

    // ── Download ─────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a workshop item to a target directory, hardlink-copies from
    /// the Steam download folder, verifies the filegroup_sha.json manifest
    /// and all file hashes, and returns a populated <see cref="FileGroup"/>.
    /// </summary>
    /// <param name="publishedFileId">Steam Workshop published file ID.</param>
    /// <param name="targetPath">Directory to download and extract to.</param>
    /// <param name="ct">CancellationToken token.</param>
    /// <returns>The downloaded file group.</returns>
    /// <exception cref="SteamShareException">Thrown when download or hash verification fails.</exception>
    public async Task<FileGroup> DownloadAsync(ulong publishedFileId, string? targetPath,
        Action<long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        LogSerilog.Information("Starting download for item {PublishedFileId}", publishedFileId);

        // Step 1: Download from workshop
        var downloadResult = await _workshop.DownloadItemAsync(publishedFileId, progressCallback, ct).ConfigureAwait(false);
        LogSerilog.Debug("Download completed, local folder: {Folder}", downloadResult.LocalFolderPath);

        // Step 2: Copy from Steam download folder to target using hardlinks, then delete source

        if (string.IsNullOrEmpty(targetPath))
        {
            targetPath = downloadResult.LocalFolderPath + ".ss." + Guid.NewGuid().ToString("N");
        }

        Directory.CreateDirectory(targetPath);
        await FileSystemHelper.CopyDirectoryAsync(
            downloadResult.LocalFolderPath, targetPath, deleteSourceAfterCopy: true, ct).ConfigureAwait(false);

        LogSerilog.Debug("Copied content to target: {TargetPath}", targetPath);

        // Step 3: Read metadata from workshop item
        var itemInfo = await _workshop.QueryItemByIdAsync(publishedFileId, ct).ConfigureAwait(false);
        if (itemInfo is null)
        {
            LogSerilog.Error("Failed to query workshop item {PublishedFileId}", publishedFileId);
            throw new WorkshopDownloadException($"Failed to query workshop item {publishedFileId}");
        }

        var metadata = string.IsNullOrEmpty(itemInfo.MetadataJson)
            ? throw new WorkshopDownloadException($"No metadata found for workshop item {publishedFileId}")
            : FileGroupMetadata.FromJson(itemInfo.MetadataJson);

        // Step 4: Verify integrity using filegroup_sha.json manifest
        if (metadata.ManifestHash is not null)
        {
            try
            {
                await VerifyFileGroupShaAsync(targetPath, metadata.ManifestHash);
                LogSerilog.Debug("Integrity verified for {PublishedFileId}: manifest hash matches",
                    publishedFileId);
            }
            catch (Exception ex)
            {
                LogSerilog.Error(ex, "Hash verification failed for item {PublishedFileId}", publishedFileId);
                //try { Directory.Delete(targetPath, true); } catch { /* best effort */ }
                throw new WorkshopDownloadException(
                    $"Hash verification failed for item {publishedFileId}: {ex.Message}", ex);
            }
        }

        // Step 5: Persist tracking
        _trackingDb.Upsert(new FileGroupTrackingEntry
        {
            PublishedFileId = publishedFileId,
            LocalPath = targetPath,
            State = DownloadState.Downloaded,
            CachedName = metadata.Name,
            CachedVisibility = itemInfo.Visibility,
            LastSyncedAt = DateTimeOffset.UtcNow
        });
        await _trackingDb.SaveAsync(ct).ConfigureAwait(false);

        LogSerilog.Information("Download completed for {Name} ({PublishedFileId})",
            metadata.Name, publishedFileId);

        // Step 6: Return FileGroup
        return FileGroup.FromMetadata(metadata, publishedFileId, itemInfo.OwnerSteamId,
            itemInfo.Visibility, targetPath);
    }

    // ── Delete ───────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes a file group from Steam Workshop and removes local tracking.
    /// </summary>
    /// <param name="publishedFileId">Steam Workshop published file ID.</param>
    /// <param name="ct">CancellationToken token.</param>
    public async Task DeleteAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        LogSerilog.Information("Deleting workshop item {PublishedFileId}", publishedFileId);

        // Step 1: Delete from workshop
        await _workshop.DeleteItemAsync(publishedFileId, ct);

        // Step 2: Remove local tracking
        _trackingDb.Remove(publishedFileId);
        await _trackingDb.SaveAsync(ct);

        LogSerilog.Information("Deleted workshop item {PublishedFileId}", publishedFileId);
    }

    // ── Visibility ───────────────────────────────────────────────────────

    /// <summary>
    /// Changes the visibility of a published workshop item and updates
    /// the local tracking database.
    /// </summary>
    /// <param name="publishedFileId">Steam Workshop published file ID.</param>
    /// <param name="visibility">Target workshop visibility.</param>
    /// <param name="ct">CancellationToken token.</param>
    /// <exception cref="FileGroupNotFoundException">
    /// Thrown when the item does not exist in the workshop.
    /// </exception>
    public async Task SetVisibilityAsync(ulong publishedFileId, WorkshopVisibility visibility,
        CancellationToken ct = default)
    {
        LogSerilog.Information("Setting visibility of {PublishedFileId} to {Visibility}",
            publishedFileId, visibility);

        await _workshop.SetItemVisibilityAsync(publishedFileId, visibility, ct);

        // Update tracking DB cache
        var entry = _trackingDb.GetByPublishedFileId(publishedFileId);
        if (entry is not null)
        {
            _trackingDb.Upsert(entry with { CachedVisibility = visibility, LastSyncedAt = DateTimeOffset.UtcNow });
            await _trackingDb.SaveAsync(ct);
        }
    }

    // ── Rename ───────────────────────────────────────────────────────────

    /// <summary>
    /// Renames a published workshop item: updates the metadata name,
    /// regenerates the workshop title, and updates tracking.
    /// Does NOT modify the directory content.
    /// </summary>
    /// <param name="publishedFileId">Steam Workshop published file ID.</param>
    /// <param name="newName">New human-readable name for the file group.</param>
    /// <param name="ct">CancellationToken token.</param>
    /// <exception cref="FileGroupNotFoundException">
    /// Thrown when the item does not exist in the workshop.
    /// </exception>
    public async Task RenameAsync(ulong publishedFileId, string newName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newName);

        var itemInfo = await _workshop.QueryItemByIdAsync(publishedFileId, ct);
        if (itemInfo is null)
        {
            throw new FileGroupNotFoundException(publishedFileId);
        }

        // Parse existing metadata and update name
        var metadata = string.IsNullOrEmpty(itemInfo.MetadataJson)
            ? new FileGroupMetadata { Name = newName }
            : FileGroupMetadata.FromJson(itemInfo.MetadataJson) with { Name = newName };

        // Regenerate workshop title from the new name
        var newTitle = ComputeWorkshopTitle(newName);

        await _workshop.UpdateItemAsync(new WorkshopItemUpdateRequest
        {
            PublishedFileId = publishedFileId,
            Title = newTitle,
            MetadataJson = metadata.ToJson()
        }, null, ct);

        // Update tracking DB
        var entry = _trackingDb.GetByPublishedFileId(publishedFileId);
        if (entry is not null)
        {
            _trackingDb.Upsert(entry with { CachedName = newName, LastSyncedAt = DateTimeOffset.UtcNow });
            await _trackingDb.SaveAsync(ct);
        }
    }

    // ── Move ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves a published workshop item to a new virtual folder path.
    /// Updates metadata only — does NOT modify the directory content.
    /// </summary>
    /// <param name="publishedFileId">Steam Workshop published file ID.</param>
    /// <param name="newVirtualFolderPath">New virtual folder path within the file group tree.</param>
    /// <param name="ct">CancellationToken token.</param>
    /// <exception cref="FileGroupNotFoundException">
    /// Thrown when the item does not exist in the workshop.
    /// </exception>
    public async Task MoveAsync(ulong publishedFileId, string newVirtualFolderPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newVirtualFolderPath);

        var itemInfo = await _workshop.QueryItemByIdAsync(publishedFileId, ct);
        if (itemInfo is null)
        {
            throw new FileGroupNotFoundException(publishedFileId);
        }

        // Parse existing metadata and update virtual folder path
        var metadata = string.IsNullOrEmpty(itemInfo.MetadataJson)
            ? new FileGroupMetadata { Name = itemInfo.Title, VirtualFolderPath = newVirtualFolderPath }
            : FileGroupMetadata.FromJson(itemInfo.MetadataJson) with { VirtualFolderPath = newVirtualFolderPath };

        await _workshop.UpdateItemAsync(new WorkshopItemUpdateRequest
        {
            PublishedFileId = publishedFileId,
            MetadataJson = metadata.ToJson()
        }, null, ct);

        // Update tracking DB (LastSyncedAt only — no path field in tracking entry)
        var entry = _trackingDb.GetByPublishedFileId(publishedFileId);
        if (entry is not null)
        {
            _trackingDb.Upsert(entry with { LastSyncedAt = DateTimeOffset.UtcNow });
            await _trackingDb.SaveAsync(ct);
        }
    }

    // ── Metadata ─────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves metadata for a published workshop item and returns
    /// a fully populated <see cref="FileGroup"/>.
    /// </summary>
    /// <param name="publishedFileId">Steam Workshop published file ID.</param>
    /// <param name="ct">CancellationToken token.</param>
    /// <returns>A <see cref="FileGroup"/> reconstructed from workshop metadata.</returns>
    /// <exception cref="FileGroupNotFoundException">
    /// Thrown when the item does not exist in the workshop.
    /// </exception>
    public async Task<FileGroup> GetMetadataAsync(ulong publishedFileId, CancellationToken ct = default)
    {
        var itemInfo = await _workshop.QueryItemByIdAsync(publishedFileId, ct);
        if (itemInfo is null)
        {
            throw new FileGroupNotFoundException(publishedFileId);
        }

        var metadata = string.IsNullOrEmpty(itemInfo.MetadataJson)
            ? throw new InvalidOperationException($"Workshop item {publishedFileId} has no metadata")
            : FileGroupMetadata.FromJson(itemInfo.MetadataJson);

        return FileGroup.FromMetadata(metadata, publishedFileId,
            itemInfo.OwnerSteamId, itemInfo.Visibility);
    }

    // ── Integrity ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the file group directory on disk matches the stored
    /// filegroup_sha.json manifest hash and all file hashes.
    /// Returns false if the directory is missing, manifest hash is null,
    /// or any hash does not match.
    /// </summary>
    /// <param name="fileGroup">The file group to verify.</param>
    /// <returns>true if all hashes match; false otherwise.</returns>
    public async Task<bool> VerifyIntegrityAsync(FileGroup fileGroup)
    {
        ArgumentNullException.ThrowIfNull(fileGroup);

        if (string.IsNullOrEmpty(fileGroup.LocalFolderPath) || string.IsNullOrEmpty(fileGroup.ManifestHash))
        {
            LogSerilog.Warning("VerifyIntegrity: missing path or manifest hash for {Name}", fileGroup.Name);
            return false;
        }

        if (!Directory.Exists(fileGroup.LocalFolderPath))
        {
            LogSerilog.Warning("VerifyIntegrity: directory not found at {Path}", fileGroup.LocalFolderPath);
            return false;
        }

        try
        {
            await VerifyFileGroupShaAsync(fileGroup.LocalFolderPath, fileGroup.ManifestHash).ConfigureAwait(false);
            LogSerilog.Debug("Integrity verified for {Name}: manifest and file hashes match", fileGroup.Name);
            return true;
        }
        catch (Exception ex)
        {
            LogSerilog.Warning(ex, "Integrity FAILED for {Name}", fileGroup.Name);
            return false;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Computes the SHA-256 hash of a single file, returned as lowercase hex.
    /// </summary>
    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var hashBytes = await sha256.ComputeHashAsync(stream).ConfigureAwait(false);
        return Convert.ToHexStringLower(hashBytes);
    }
}
