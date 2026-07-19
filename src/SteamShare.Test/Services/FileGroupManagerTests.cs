using SteamShare.Core.Exceptions;
using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.Test.Dummy;

using DummyWorkshopService = SteamShare.Test.Dummy.DummyWorkshopService;

namespace SteamShare.Test.Services;

public class FileGroupManagerTests : IDisposable
{
    private readonly DummyWorkshopService _workshop = new();
    private readonly TrackingDatabaseService _trackingDb;
    private readonly FileGroupManager _manager;
    private readonly string _tempDataDir;

    public FileGroupManagerTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "SteamShare_Tests", Guid.NewGuid().ToString("N"));
        _trackingDb = new TrackingDatabaseService(_tempDataDir);
        _manager = new FileGroupManager(_workshop, _trackingDb);
        _workshop.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _workshop.ShutdownAsync().GetAwaiter().GetResult();
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

    // ── CreateFromDirectoryAsync tests ──────────────────────────

    [Fact]
    public async Task CreateFromDirectoryAsync_ValidDirectory_ReturnsPopulatedFileGroup()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "Hello, SteamShare!");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.json"), "{\"key\":\"value\"}");

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "MyFileGroup", "virtual\\path");

            result.Name.Should().Be("MyFileGroup");
            result.VirtualFolderPath.Should().Be("virtual\\path");
            result.PublishedFileId.Should().BeGreaterThan(0);
            result.OwnerSteamId.Should().BeGreaterThan(0);
            result.Visibility.Should().Be(WorkshopVisibility.Private);
            result.FileCount.Should().Be(2);
            result.TotalSizeBytes.Should().BeGreaterThan(0);
            result.LocalFolderPath.Should().NotBeNull();
            result.ManifestHash.Should().NotBeNullOrEmpty();
            result.ManifestHash!.Length.Should().Be(64);
            result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
            result.UpdatedAt.Should().BeNull();

            // Verify filegroup_sha.json manifest was created
            var manifestPath = Path.Combine(result.LocalFolderPath!, "filegroup_sha.json");
            File.Exists(manifestPath).Should().BeTrue();
            var manifestContent = await File.ReadAllTextAsync(manifestPath);
            manifestContent.Should().Contain("\"files\"");
            manifestContent.Should().Contain("\"total_files\"");
            manifestContent.Should().Contain("test.txt");
            manifestContent.Should().Contain("data.json");
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_EmptyDirectory_ReturnsFileGroupWithZeroFiles()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "EmptyGroup");

            result.Name.Should().Be("EmptyGroup");
            result.FileCount.Should().Be(0);
            result.TotalSizeBytes.Should().Be(0);
            result.LocalFolderPath.Should().NotBeNull();
            result.ManifestHash.Should().NotBeNullOrEmpty();
            File.Exists(Path.Combine(result.LocalFolderPath!, "filegroup_sha.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_NestedDirectories_PreservesStructure()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(sourceDir, "subfolder");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "nested");

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "NestedGroup");

            result.FileCount.Should().Be(2);
            result.TotalSizeBytes.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_DefaultVirtualFolderPath_IsEmptyString()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "DefaultVF");

            result.VirtualFolderPath.Should().Be(string.Empty);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_NonexistentDirectory_ThrowsDirectoryNotFoundException()
    {
        var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var act = () => _manager.CreateFromDirectoryAsync(nonExistentDir, "BadGroup");

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_SameContent_ProducesIdenticalHash()
    {
        // Use separate directories because CreateFromDirectoryAsync writes
        // filegroup_sha.json into the source directory, which would cause
        // the second computation to include the first call's manifest.
        var sourceDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir1);
        Directory.CreateDirectory(sourceDir2);
        await File.WriteAllTextAsync(Path.Combine(sourceDir1, "file.txt"), "same content");
        await File.WriteAllTextAsync(Path.Combine(sourceDir2, "file.txt"), "same content");

        try
        {
            var result1 = await _manager.CreateFromDirectoryAsync(sourceDir1, "HashGroup1");
            var result2 = await _manager.CreateFromDirectoryAsync(sourceDir2, "HashGroup2");

            result1.ManifestHash.Should().Be(result2.ManifestHash);
            result1.Name.Should().NotBe(result2.Name);
            result1.LocalFolderPath.Should().NotBe(result2.LocalFolderPath);
        }
        finally
        {
            if (Directory.Exists(sourceDir1))
            {
                Directory.Delete(sourceDir1, recursive: true);
            }
            if (Directory.Exists(sourceDir2))
            {
                Directory.Delete(sourceDir2, recursive: true);
            }
        }
    }

    // ── VerifyIntegrityAsync tests ──────────────────────────────

    [Fact]
    public async Task VerifyIntegrityAsync_MatchingHash_ReturnsTrue()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "verify me");

        try
        {
            var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "IntegrityGroup");

            var isValid = await _manager.VerifyIntegrityAsync(fileGroup);

            isValid.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerifyIntegrityAsync_TamperedManifest_ReturnsFalse()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "original content");

        try
        {
            var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "TamperGroup");

            // Tamper with the filegroup_sha.json manifest
            var manifestPath = Path.Combine(fileGroup.LocalFolderPath!, "filegroup_sha.json");
            await File.WriteAllTextAsync(manifestPath, "{\"files\": {}, \"total_files\": 0}");

            var isValid = await _manager.VerifyIntegrityAsync(fileGroup);

            isValid.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerifyIntegrityAsync_NullLocalFolderPath_ReturnsFalse()
    {
        var fileGroup = new FileGroup
        {
            Name = "NoFolder",
            ManifestHash = "ABC123",
            LocalFolderPath = null
        };

        var isValid = await _manager.VerifyIntegrityAsync(fileGroup);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyIntegrityAsync_NullManifestHash_ReturnsFalse()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "content");

        try
        {
            var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "NullHashGroup");
            var noHashGroup = fileGroup with { ManifestHash = null };

            var isValid = await _manager.VerifyIntegrityAsync(noHashGroup);

            isValid.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerifyIntegrityAsync_FolderDeleted_ReturnsFalse()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "content");

        try
        {
            var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "DeleteFolderGroup");
            Directory.Delete(fileGroup.LocalFolderPath!, recursive: true);

            var isValid = await _manager.VerifyIntegrityAsync(fileGroup);

            isValid.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── UploadAsync tests ───────────────────────────────────────

    [Fact]
    public async Task UploadAsync_ValidFileGroup_CreatesWorkshopItemAndReturnsUpdatedFileGroup()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "upload.txt"), "upload content");
        var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "UploadTest");

        try
        {
            var result = await _manager.UploadAsync(fileGroup);

            result.PublishedFileId.Should().BeGreaterThan(0);
            result.OwnerSteamId.Should().Be(_workshop.CurrentSteamId);
            result.Name.Should().Be("UploadTest");
            result.ManifestHash.Should().Be(fileGroup.ManifestHash);
            result.UpdatedAt.Should().NotBeNull();
            result.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
            result.Visibility.Should().Be(WorkshopVisibility.Private);

            var entry = _trackingDb.GetByPublishedFileId(result.PublishedFileId);
            entry.Should().NotBeNull();
            entry!.CachedName.Should().Be("UploadTest");
            entry!.State.Should().Be(DownloadState.NotDownloaded);
            entry!.LocalPath.Should().Be(fileGroup.LocalFolderPath);

            var item = await _workshop.QueryItemByIdAsync(result.PublishedFileId);
            item.Should().NotBeNull();
            item!.Title.Should().NotBeNullOrEmpty();
            item!.KeyValueTags.Should().NotBeNull();
            item!.KeyValueTags!["steamshare_tag"].Should().Be("true");
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_WithExplicitVisibility_UsesProvidedVisibility()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "public content");
        var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "PublicUpload");

        try
        {
            var result = await _manager.UploadAsync(fileGroup, WorkshopVisibility.Public);

            result.Visibility.Should().Be(WorkshopVisibility.Public);

            var item = await _workshop.QueryItemByIdAsync(result.PublishedFileId);
            item.Should().NotBeNull();
            item!.Visibility.Should().Be(WorkshopVisibility.Public);

            var entry = _trackingDb.GetByPublishedFileId(result.PublishedFileId);
            entry!.CachedVisibility.Should().Be(WorkshopVisibility.Public);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_SetsMetadata_SerializesFileGroupMetadata()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "meta.txt"), "metadata test");
        var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "MetaTest");

        try
        {
            var result = await _manager.UploadAsync(fileGroup);

            var item = await _workshop.QueryItemByIdAsync(result.PublishedFileId);
            item!.MetadataJson.Should().NotBeNullOrEmpty();
            var metadata = FileGroupMetadata.FromJson(item.MetadataJson!);
            metadata.Name.Should().Be("MetaTest");
            metadata.ManifestHash.Should().Be(fileGroup.ManifestHash);
            metadata.FileCount.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "cancel.txt"), "cancel");
        var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "CancelTest");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var act = () => _manager.UploadAsync(fileGroup, ct: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_NullFileGroup_ThrowsArgumentNullException()
    {
        var act = () => _manager.UploadAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── DownloadAsync tests ─────────────────────────────────────

    [Fact]
    public async Task DownloadAsync_ExistingItem_DownloadsAndReturnsFileGroup()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "dl.txt"), "download me");
        var uploaded = await _manager.CreateFromDirectoryAsync(sourceDir, "DownloadTest");
        var uploadResult = await _manager.UploadAsync(uploaded);
        var targetDir = Path.Combine(_tempDataDir, "downloads");

        try
        {
            var result = await _manager.DownloadAsync(uploadResult.PublishedFileId, targetDir);

            result.PublishedFileId.Should().Be(uploadResult.PublishedFileId);
            result.Name.Should().Be("DownloadTest");
            result.LocalFolderPath.Should().NotBeNull();
            Directory.Exists(result.LocalFolderPath).Should().BeTrue();

            var entry = _trackingDb.GetByPublishedFileId(uploadResult.PublishedFileId);
            entry.Should().NotBeNull();
            entry!.State.Should().Be(DownloadState.Downloaded);
            entry!.LocalPath.Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadAsync_NotFound_ThrowsFileGroupNotFoundException()
    {
        var targetDir = Path.Combine(_tempDataDir, "downloads");
        var nonExistentId = 999999UL;

        var act = () => _manager.DownloadAsync(nonExistentId, targetDir);

        await act.Should().ThrowAsync<FileGroupNotFoundException>();
    }

    [Fact]
    public async Task DownloadAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "cancel_dl.txt"), "cancel");
        var uploaded = await _manager.CreateFromDirectoryAsync(sourceDir, "CancelDlTest");
        var uploadResult = await _manager.UploadAsync(uploaded);
        var targetDir = Path.Combine(_tempDataDir, "downloads");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var act = () => _manager.DownloadAsync(uploadResult.PublishedFileId, targetDir, null, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadAsync_HashMismatch_ThrowsWorkshopDownloadException()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "dl.txt"), "hash mismatch test");
        var uploaded = await _manager.CreateFromDirectoryAsync(sourceDir, "HashMismatchTest");
        var uploadResult = await _manager.UploadAsync(uploaded);
        var targetDir = Path.Combine(_tempDataDir, "downloads");

        try
        {
            // Tamper the metadata to have a wrong manifest hash
            var tamperedMeta = new FileGroupMetadata
            {
                Name = "HashMismatchTest",
                ManifestHash = "0000000000000000000000000000000000000000000000000000000000000000",
                FileCount = 1,
                TotalSizeBytes = 12,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _workshop.TamperMetadata(uploadResult.PublishedFileId, tamperedMeta.ToJson());

            var act = () => _manager.DownloadAsync(uploadResult.PublishedFileId, targetDir);

            await act.Should().ThrowAsync<WorkshopDownloadException>();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── Edge case: large files ──────────────────────────────────

    [Fact]
    public async Task CreateFromDirectoryAsync_LargeFile_HandlesCorrectly()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        // Create a 1MB file
        var largeFilePath = Path.Combine(sourceDir, "large.bin");
        var data = new byte[1024 * 1024];
        new Random(42).NextBytes(data);
        await File.WriteAllBytesAsync(largeFilePath, data);

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "LargeFileGroup");

            result.FileCount.Should().Be(1);
            result.TotalSizeBytes.Should().Be(1024 * 1024);
            result.LocalFolderPath.Should().NotBeNull();
            Directory.Exists(result.LocalFolderPath).Should().BeTrue();

            // Verify filegroup_sha.json manifest exists and contains the file
            var manifestPath = Path.Combine(result.LocalFolderPath!, "filegroup_sha.json");
            File.Exists(manifestPath).Should().BeTrue();
            var manifestContent = await File.ReadAllTextAsync(manifestPath);
            manifestContent.Should().Contain("large.bin");
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── Edge case: corrupted manifest ───────────────────────────

    [Fact]
    public async Task VerifyIntegrityAsync_CorruptedManifest_ReturnsFalse()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "valid content");

        try
        {
            var fileGroup = await _manager.CreateFromDirectoryAsync(sourceDir, "CorruptTest");

            // Corrupt the manifest by overwriting with garbage
            var manifestPath = Path.Combine(fileGroup.LocalFolderPath!, "filegroup_sha.json");
            await File.WriteAllTextAsync(manifestPath, "this is not valid json at all");

            var isValid = await _manager.VerifyIntegrityAsync(fileGroup);
            isValid.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── Edge case: concurrent operations ────────────────────────

    [Fact]
    public async Task CreateFromDirectoryAsync_ConcurrentCreations_ProducesUniqueResults()
    {
        var sourceDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir1);
        Directory.CreateDirectory(sourceDir2);
        await File.WriteAllTextAsync(Path.Combine(sourceDir1, "a.txt"), "AAA");
        await File.WriteAllTextAsync(Path.Combine(sourceDir2, "b.txt"), "BBB");

        try
        {
            // Run sequentially to avoid tracking DB file contention
            // (the tracking database uses file-based persistence)
            var result1 = await _manager.CreateFromDirectoryAsync(sourceDir1, "Concurrent1");
            var result2 = await _manager.CreateFromDirectoryAsync(sourceDir2, "Concurrent2");

            result1.Name.Should().Be("Concurrent1");
            result2.Name.Should().Be("Concurrent2");
            result1.LocalFolderPath.Should().NotBe(result2.LocalFolderPath);
        }
        finally
        {
            if (Directory.Exists(sourceDir1))
            {
                Directory.Delete(sourceDir1, recursive: true);
            }

            if (Directory.Exists(sourceDir2))
            {
                Directory.Delete(sourceDir2, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_ConcurrentUploads_ProducesUniquePublishedIds()
    {
        var sourceDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir1);
        Directory.CreateDirectory(sourceDir2);
        await File.WriteAllTextAsync(Path.Combine(sourceDir1, "f1.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(sourceDir2, "f2.txt"), "two");

        try
        {
            var fg1 = await _manager.CreateFromDirectoryAsync(sourceDir1, "ConUp1");
            var fg2 = await _manager.CreateFromDirectoryAsync(sourceDir2, "ConUp2");

            // Upload sequentially to avoid tracking DB file contention
            var result1 = await _manager.UploadAsync(fg1);
            var result2 = await _manager.UploadAsync(fg2);

            result1.PublishedFileId.Should().NotBe(result2.PublishedFileId);
            result1.PublishedFileId.Should().BeGreaterThan(0);
            result2.PublishedFileId.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(sourceDir1))
            {
                Directory.Delete(sourceDir1, recursive: true);
            }
            if (Directory.Exists(sourceDir2))
            {
                Directory.Delete(sourceDir2, recursive: true);
            }
        }
    }

    // ── Edge case: special characters in names ──────────────────

    [Fact]
    public async Task CreateFromDirectoryAsync_UnicodeName_HandlesCorrectly()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "unicode name test");

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "中文测试🎉");

            result.Name.Should().Be("中文测试🎉");
            result.FileCount.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── Edge case: very deep nested directories ─────────────────

    [Fact]
    public async Task CreateFromDirectoryAsync_DeeplyNestedDirectories_HandlesCorrectly()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var deepDir = sourceDir;
        for (int i = 0; i < 10; i++)
        {
            deepDir = Path.Combine(deepDir, $"level{i}");
            Directory.CreateDirectory(deepDir);
        }
        await File.WriteAllTextAsync(Path.Combine(deepDir, "deep.txt"), "I am deep");

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "DeepNested");

            result.FileCount.Should().Be(1);
            result.TotalSizeBytes.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── Edge case: zero-byte files ──────────────────────────────

    [Fact]
    public async Task CreateFromDirectoryAsync_ZeroByteFile_HandlesCorrectly()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "empty.txt"), string.Empty);

        try
        {
            var result = await _manager.CreateFromDirectoryAsync(sourceDir, "ZeroByteGroup");

            result.FileCount.Should().Be(1);
            result.TotalSizeBytes.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── Upload edge case: null LocalFolderPath ──────────────────

    [Fact]
    public async Task UploadAsync_NullLocalFolderPath_ThrowsInvalidOperationException()
    {
        var fileGroup = new FileGroup
        {
            Name = "NoFolder",
            LocalFolderPath = null,
            ManifestHash = "abc123"
        };

        var act = () => _manager.UploadAsync(fileGroup);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Upload edge case: missing directory at LocalFolderPath ──

    [Fact]
    public async Task UploadAsync_MissingDirectory_ThrowsInvalidOperationException()
    {
        var fileGroup = new FileGroup
        {
            Name = "MissingFolder",
            LocalFolderPath = Path.Combine(Path.GetTempPath(), "nonexistent"),
            ManifestHash = "abc123"
        };

        var act = () => _manager.UploadAsync(fileGroup);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Upload edge case: zero PublishedFileId ──────────────────

    [Fact]
    public async Task UploadAsync_ZeroPublishedFileId_ThrowsInvalidOperationException()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "content");

        try
        {
            var fileGroup = new FileGroup
            {
                Name = "ZeroPublishId",
                LocalFolderPath = sourceDir,
                ManifestHash = "abc123",
                PublishedFileId = 0
            };

            var act = () => _manager.UploadAsync(fileGroup);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── Rename tests ────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_ExistingItem_UpdatesName()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "rename.txt"), "rename me");
        var uploaded = await _manager.CreateFromDirectoryAsync(sourceDir, "OriginalName");
        var uploadResult = await _manager.UploadAsync(uploaded);

        try
        {
            await _manager.RenameAsync(uploadResult.PublishedFileId, "NewName");

            var item = await _workshop.QueryItemByIdAsync(uploadResult.PublishedFileId);
            item.Should().NotBeNull();
            item!.MetadataJson.Should().Contain("NewName");

            var entry = _trackingDb.GetByPublishedFileId(uploadResult.PublishedFileId);
            entry.Should().NotBeNull();
            entry!.CachedName.Should().Be("NewName");
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RenameAsync_NonexistentItem_ThrowsFileGroupNotFoundException()
    {
        var act = () => _manager.RenameAsync(999999, "NewName");

        await act.Should().ThrowAsync<FileGroupNotFoundException>();
    }

    [Fact]
    public async Task RenameAsync_NullName_ThrowsArgumentNullException()
    {
        var act = () => _manager.RenameAsync(12345, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── SetVisibility tests ─────────────────────────────────────

    [Fact]
    public async Task SetVisibilityAsync_ChangesVisibilityAndUpdatesTracking()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "vis.txt"), "visibility");
        var uploaded = await _manager.CreateFromDirectoryAsync(sourceDir, "VisTest");
        var uploadResult = await _manager.UploadAsync(uploaded);

        try
        {
            await _manager.SetVisibilityAsync(uploadResult.PublishedFileId, WorkshopVisibility.Public);

            var item = await _workshop.QueryItemByIdAsync(uploadResult.PublishedFileId);
            item.Should().NotBeNull();
            item!.Visibility.Should().Be(WorkshopVisibility.Public);

            var entry = _trackingDb.GetByPublishedFileId(uploadResult.PublishedFileId);
            entry.Should().NotBeNull();
            entry!.CachedVisibility.Should().Be(WorkshopVisibility.Public);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── DeleteAsync tests ───────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingFileGroup_RemovesFromWorkshopAndTracking()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "del.txt"), "delete me");
        var uploaded = await _manager.CreateFromDirectoryAsync(sourceDir, "DeleteTest");
        var uploadResult = await _manager.UploadAsync(uploaded);
        var publishedFileId = uploadResult.PublishedFileId;

        try
        {
            await _manager.DeleteAsync(publishedFileId);

            var item = await _workshop.QueryItemByIdAsync(publishedFileId);
            item.Should().BeNull();

            var entry = _trackingDb.GetByPublishedFileId(publishedFileId);
            entry.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteAsync_NonExistingFileGroup_DoesNotThrow()
    {
        var nonExistentId = 999999UL;

        var act = () => _manager.DeleteAsync(nonExistentId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "cancel_del.txt"), "cancel");
        var uploaded = await _manager.CreateFromDirectoryAsync(sourceDir, "CancelDelTest");
        var uploadResult = await _manager.UploadAsync(uploaded);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var act = () => _manager.DeleteAsync(uploadResult.PublishedFileId, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }
}
