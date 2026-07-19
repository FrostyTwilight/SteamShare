using SteamShare.Core.Services;
using SteamShare.Test.Dummy;

using DummyWorkshopService = SteamShare.Test.Dummy.DummyWorkshopService;

namespace SteamShare.Test.Services;

public class VisibilityServiceTests : IDisposable
{
    private readonly DummyWorkshopService _workshop = new();
    private readonly TrackingDatabaseService _trackingDb;
    private readonly FileGroupManager _fileGroupManager;
    private readonly VisibilityService _service;
    private readonly string _tempDataDir;

    public VisibilityServiceTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "SteamShare_Tests", Guid.NewGuid().ToString("N"));
        _trackingDb = new TrackingDatabaseService(_tempDataDir);
        _fileGroupManager = new FileGroupManager(_workshop, _trackingDb);
        _service = new VisibilityService(_fileGroupManager);
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

    private async Task<ulong> CreateUploadedItemAsync(WorkshopVisibility visibility)
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "visibility test");
            var fileGroup = await _fileGroupManager.CreateFromDirectoryAsync(sourceDir, "VisTest");
            var result = await _fileGroupManager.UploadAsync(fileGroup, visibility);
            return result.PublishedFileId;
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }
        }
    }

    // ── ChangeVisibilityAsync tests ──────────────────────────────────

    [Fact]
    public async Task ChangeVisibilityAsync_PrivateToUnlistedWithoutConfirmation_Allowed()
    {
        // Arrange
        var id = await CreateUploadedItemAsync(WorkshopVisibility.Private);

        // Act
        await _service.ChangeVisibilityAsync(id, WorkshopVisibility.Unlisted, confirmed: false);

        // Assert
        var item = await _workshop.QueryItemByIdAsync(id);
        item.Should().NotBeNull();
        item!.Visibility.Should().Be(WorkshopVisibility.Unlisted);
    }

    [Fact]
    public async Task ChangeVisibilityAsync_PrivateToPublicWithoutConfirmation_ThrowsInvalidOperationException()
    {
        // Arrange
        var id = await CreateUploadedItemAsync(WorkshopVisibility.Private);

        // Act
        var act = () => _service.ChangeVisibilityAsync(id, WorkshopVisibility.Public, confirmed: false);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Verify visibility was NOT changed
        var item = await _workshop.QueryItemByIdAsync(id);
        item.Should().NotBeNull();
        item!.Visibility.Should().Be(WorkshopVisibility.Private);
    }

    [Fact]
    public async Task ChangeVisibilityAsync_PublicToPrivateWithConfirmation_Allowed()
    {
        // Arrange
        var id = await CreateUploadedItemAsync(WorkshopVisibility.Public);

        // Act
        await _service.ChangeVisibilityAsync(id, WorkshopVisibility.Private, confirmed: true);

        // Assert
        var item = await _workshop.QueryItemByIdAsync(id);
        item.Should().NotBeNull();
        item!.Visibility.Should().Be(WorkshopVisibility.Private);
    }

    [Fact]
    public async Task ChangeVisibilityAsync_PrivateToPublicWithConfirmation_Allowed()
    {
        // Arrange
        var id = await CreateUploadedItemAsync(WorkshopVisibility.Private);

        // Act
        await _service.ChangeVisibilityAsync(id, WorkshopVisibility.Public, confirmed: true);

        // Assert
        var item = await _workshop.QueryItemByIdAsync(id);
        item.Should().NotBeNull();
        item!.Visibility.Should().Be(WorkshopVisibility.Public);
    }

    [Fact]
    public async Task ChangeVisibilityAsync_UnlistedToPublicWithoutConfirmation_ThrowsInvalidOperationException()
    {
        // Arrange
        var id = await CreateUploadedItemAsync(WorkshopVisibility.Unlisted);

        // Act
        var act = () => _service.ChangeVisibilityAsync(id, WorkshopVisibility.Public, confirmed: false);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── GetVisibilityWarning tests ───────────────────────────────────

    [Fact]
    public void GetVisibilityWarning_PrivateToPublic_ReturnsWarningKey()
    {
        var result = _service.GetVisibilityWarning(WorkshopVisibility.Private, WorkshopVisibility.Public);

        result.Should().NotBeNullOrEmpty();
        result.Should().Be("VisibilityWarning.PrivateToPublic");
    }

    [Fact]
    public void GetVisibilityWarning_UnlistedToPublic_ReturnsWarningKey()
    {
        var result = _service.GetVisibilityWarning(WorkshopVisibility.Unlisted, WorkshopVisibility.Public);

        result.Should().NotBeNullOrEmpty();
        result.Should().Be("VisibilityWarning.UnlistedToPublic");
    }

    [Fact]
    public void GetVisibilityWarning_PrivateToUnlisted_ReturnsEmpty()
    {
        var result = _service.GetVisibilityWarning(WorkshopVisibility.Private, WorkshopVisibility.Unlisted);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibilityWarning_PublicToPrivate_ReturnsEmpty()
    {
        var result = _service.GetVisibilityWarning(WorkshopVisibility.Public, WorkshopVisibility.Private);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibilityWarning_PublicToUnlisted_ReturnsEmpty()
    {
        var result = _service.GetVisibilityWarning(WorkshopVisibility.Public, WorkshopVisibility.Unlisted);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibilityWarning_UnlistedToPrivate_ReturnsEmpty()
    {
        var result = _service.GetVisibilityWarning(WorkshopVisibility.Unlisted, WorkshopVisibility.Private);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibilityWarning_SameVisibility_ReturnsEmpty()
    {
        var result = _service.GetVisibilityWarning(WorkshopVisibility.Public, WorkshopVisibility.Public);

        result.Should().BeEmpty();
    }
}
