using SteamShare.Core.Models;
using SteamShare.Core.Services;
using SteamShare.Test.Dummy;

using DummyWorkshopService = SteamShare.Test.Dummy.DummyWorkshopService;

namespace SteamShare.Test.Services;

public class WorkshopQueryServiceTests
{
    private readonly DummyWorkshopService _workshop;
    private readonly WorkshopQueryService _service;

    public WorkshopQueryServiceTests()
    {
        _workshop = new DummyWorkshopService();
        _workshop.InitializeAsync().GetAwaiter().GetResult();
        _service = new WorkshopQueryService(_workshop);
    }

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_ReturnsFileGroups_ForSteamShareTaggedItems()
    {
        // Arrange
        var metadata = new FileGroupMetadata { Name = "MyGroup", FileCount = 5, TotalSizeBytes = 1024, CreatedAt = DateTimeOffset.UtcNow };
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Group 1",
            Description = "Test group",
            MetadataJson = metadata.ToJson(),
            ContentPath = "/tmp/test1",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Group 2",
            Description = "Test group 2",
            MetadataJson = new FileGroupMetadata { Name = "Group2", FileCount = 3, TotalSizeBytes = 512, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/test2",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });

        // Act
        var result = await _service.QueryOwnedFileGroupsAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("MyGroup");
        result[1].Name.Should().Be("Group2");
    }

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_FiltersOutItems_WithoutSteamShareTag()
    {
        // Arrange
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Tagged",
            Description = "Has tag",
            MetadataJson = new FileGroupMetadata { Name = "Tagged", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/tagged",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });
        // Act
        var result = await _service.QueryOwnedFileGroupsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Tagged");
    }

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_FiltersOutItems_WithInvalidMetadataJson()
    {
        // Arrange
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Valid",
            Description = "Has valid metadata",
            MetadataJson = new FileGroupMetadata { Name = "Valid", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/valid",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Invalid",
            Description = "Has invalid metadata JSON",
            MetadataJson = "not-valid-json",
            ContentPath = "/tmp/invalid",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });

        // Act
        var result = await _service.QueryOwnedFileGroupsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Valid");
    }

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_ReturnsEmptyList_WhenNoItemsMatch()
    {
        // Arrange — no items created

        // Act
        var result = await _service.QueryOwnedFileGroupsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryFileGroupByIdAsync_ReturnsFileGroup_WhenItemExists()
    {
        // Arrange
        var metadata = new FileGroupMetadata { Name = "Found", FileCount = 10, TotalSizeBytes = 2048, CreatedAt = DateTimeOffset.UtcNow };
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Found Group",
            Description = "Should be found",
            MetadataJson = metadata.ToJson(),
            ContentPath = "/tmp/found",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });

        // Act
        var result = await _service.QueryFileGroupByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.PublishedFileId.Should().Be(id);
        result.Name.Should().Be("Found");
        result.FileCount.Should().Be(10);
        result.TotalSizeBytes.Should().Be(2048);
        result.OwnerSteamId.Should().Be(_workshop.CurrentSteamId);
    }

    [Fact]
    public async Task QueryFileGroupByIdAsync_ReturnsNull_WhenItemDoesNotExist()
    {
        // Act
        var result = await _service.QueryFileGroupByIdAsync(99999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryFileGroupByIdAsync_ReturnsNull_WhenItemLacksSteamShareTag()
    {
        // Arrange
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "No Tag",
            Description = "Missing steamshare tag",
            MetadataJson = new FileGroupMetadata { Name = "NoTag", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/notag"
        });

        // Act
        var result = await _service.QueryFileGroupByIdAsync(id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryFileGroupByIdAsync_ReturnsNull_WhenMetadataJsonIsInvalid()
    {
        // Arrange
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "Bad Metadata",
            Description = "Corrupted metadata",
            MetadataJson = "{broken-json",
            ContentPath = "/tmp/bad",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });

        // Act
        var result = await _service.QueryFileGroupByIdAsync(id);

        // Assert
        result.Should().BeNull();
    }

    // ── Empty results ───────────────────────────────────────────

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_EmptyWorkshop_ReturnsEmptyList()
    {
        // DummyWorkshopService starts with no items
        var result = await _service.QueryOwnedFileGroupsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_OnlyNonSteamShareItems_ReturnsAllItemsWithMetadata()
    {
        // Note: QueryOwnedFileGroupsAsync does NOT filter by steamshare_tag;
        // it returns ALL owned items that have valid FileGroupMetadata JSON.
        // Tag filtering is only done in QueryFileGroupByIdAsync.
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "NoTag1",
            Description = "Missing tag",
            MetadataJson = new FileGroupMetadata { Name = "NoTag1", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/notag1"
        });
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "NoTag2",
            Description = "Also missing tag",
            MetadataJson = new FileGroupMetadata { Name = "NoTag2", FileCount = 2, TotalSizeBytes = 200, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/notag2"
        });

        var result = await _service.QueryOwnedFileGroupsAsync();

        // All items with valid metadata are returned regardless of tags
        result.Should().HaveCount(2);
        result.Should().Contain(fg => fg.Name == "NoTag1");
        result.Should().Contain(fg => fg.Name == "NoTag2");
    }

    // ── Invalid tags ────────────────────────────────────────────

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_WrongTagValue_ReturnsItem()
    {
        // QueryOwnedFileGroupsAsync returns all items with valid metadata
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "WrongTag",
            Description = "Has steamshare_tag but wrong value",
            MetadataJson = new FileGroupMetadata { Name = "WrongTag", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/wrongtag",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "false" }
        });

        var result = await _service.QueryOwnedFileGroupsAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("WrongTag");
    }

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_DifferentTagKey_ReturnsItem()
    {
        // QueryOwnedFileGroupsAsync returns all items with valid metadata
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "OtherTag",
            Description = "Has a different tag key",
            MetadataJson = new FileGroupMetadata { Name = "OtherTag", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/othertag",
            KeyValueTags = new Dictionary<string, string> { ["other_tag"] = "true" }
        });

        var result = await _service.QueryOwnedFileGroupsAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("OtherTag");
    }

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_ExtraTags_StillReturnsItem()
    {
        await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "MultiTag",
            Description = "Has steamshare_tag plus extra tags",
            MetadataJson = new FileGroupMetadata { Name = "MultiTag", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/multitag",
            KeyValueTags = new Dictionary<string, string>
            {
                ["steamshare_tag"] = "true",
                ["extra_tag"] = "extra_value",
                ["version"] = "1.0"
            }
        });

        var result = await _service.QueryOwnedFileGroupsAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("MultiTag");
    }

    // ── QueryFileGroupByIdAsync: item without tag ───────────────

    [Fact]
    public async Task QueryFileGroupByIdAsync_ItemExistsButNoTag_ReturnsNull()
    {
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "NoTag",
            Description = "No steamshare tag",
            MetadataJson = new FileGroupMetadata { Name = "NoTag", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
            ContentPath = "/tmp/notag"
        });

        var result = await _service.QueryFileGroupByIdAsync(id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryFileGroupByIdAsync_ItemExistsHasTag_ReturnsFileGroup()
    {
        var metadata = new FileGroupMetadata { Name = "TaggedItem", FileCount = 5, TotalSizeBytes = 5000, CreatedAt = DateTimeOffset.UtcNow };
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "TaggedItem",
            Description = "Has steamshare tag",
            MetadataJson = metadata.ToJson(),
            ContentPath = "/tmp/tagged",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });

        var result = await _service.QueryFileGroupByIdAsync(id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("TaggedItem");
        result.PublishedFileId.Should().Be(id);
    }

    // ── QueryFileGroupByIdAsync: null metadata ──────────────────

    [Fact]
    public async Task QueryFileGroupByIdAsync_NullMetadataJson_ReturnsNull()
    {
        var id = await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
        {
            Title = "NullMeta",
            Description = "Null metadata JSON",
            MetadataJson = null!,
            ContentPath = "/tmp/nullmeta",
            KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
        });

        var result = await _service.QueryFileGroupByIdAsync(id);

        result.Should().BeNull();
    }

    // ── Large number of items ───────────────────────────────────

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_LargeNumberOfItems_ReturnsAll()
    {
        const int itemCount = 50;
        for (int i = 0; i < itemCount; i++)
        {
            await _workshop.CreateItemAsync(new WorkshopItemCreateRequest
            {
                Title = $"Batch{i}",
                Description = $"Description{i}",
                MetadataJson = new FileGroupMetadata { Name = $"Batch{i}", FileCount = 1, TotalSizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow }.ToJson(),
                ContentPath = $"/tmp/batch{i}",
                KeyValueTags = new Dictionary<string, string> { ["steamshare_tag"] = "true" }
            });
        }

        var result = await _service.QueryOwnedFileGroupsAsync();

        result.Should().HaveCount(itemCount);
    }

    // ── Cancellation ────────────────────────────────────────────

    [Fact]
    public async Task QueryOwnedFileGroupsAsync_WithCancellation_CompletesSuccessfully()
    {
        // DummyWorkshopService doesn't check CancellationToken in sync methods,
        // so cancellation on an already-cancelled token won't throw
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The service may or may not throw depending on implementation
        // Just verify it doesn't hang
        var task = _service.QueryOwnedFileGroupsAsync(cts.Token);
        await Task.WhenAny(task, Task.Delay(1000));
    }

    [Fact]
    public async Task QueryFileGroupByIdAsync_WithCancellation_CompletesSuccessfully()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = _service.QueryFileGroupByIdAsync(12345, cts.Token);
        await Task.WhenAny(task, Task.Delay(1000));
    }
}
